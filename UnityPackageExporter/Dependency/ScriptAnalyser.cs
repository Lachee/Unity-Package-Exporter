using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UnityPackageExporter.Dependency
{
    class ScriptAnalyser : IDisposable
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetLogger("ScriptAnalyser");

        private AdhocWorkspace workspace;
        private Solution solution;
        private Project project;
        private Dictionary<string, Document> documents = new Dictionary<string, Document>();
        private Dictionary<string, string[]> dependencyCache = new Dictionary<string, string[]>();

        public string ProjectPath { get; }

        public ScriptAnalyser(string projectPath)
        {
            var mscorlib = PortableExecutableReference.CreateFromFile(typeof(object).Assembly.Location);
            
            ProjectPath = projectPath;
            workspace = new AdhocWorkspace();
            documents = new Dictionary<string, Document>();

            //Prepare solution
            var solId = SolutionId.CreateNewId();
            var solutionInfo = SolutionInfo.Create(solId, VersionStamp.Default);
            solution = workspace.AddSolution(solutionInfo);

            //Prepare the project
            project = workspace.AddProject("Sample", LanguageNames.CSharp);
            project = project.AddMetadataReference(mscorlib)
                                .WithParseOptions(((CSharpParseOptions)project.ParseOptions)
                                .WithPreprocessorSymbols("UNITY_EDITOR"));
        }

        /// <summary>Adds a source code directly as a reference</summary>
        public void AddSource(string name, string source)
        {
            Logger.Trace("Adding source {0}", name);

            dependencyCache.Clear();
            var src = SourceText.From(source);
            var doc = project.AddDocument(name, src);
            project = doc.Project;
            documents.Add(name, doc);
        }

        /// <summary>Adds a file to valid source documents</summary>
        public async Task<bool> AddFileAsync(string file)
        {
            Logger.Trace("Adding file {0}", file);
            if (!File.Exists(file))
            {
                Logger.Error("File {0} does not exist", file);
                return false;
            }

            var name = Path.GetFileName(file);
            var fileContent = await File.ReadAllTextAsync(file);
            if (string.IsNullOrWhiteSpace(fileContent))
            {
                Logger.Error("File {0} source is empty", file);
                return false;
            }

            var src = SourceText.From(fileContent);
            if (src == null)
            {
                Logger.Error("File {0} source is invalid", file);
                return false;
            }

            dependencyCache.Clear();
            var doc = project.AddDocument(name, src, filePath: file);
            if (src == null)
            {
                Logger.Error("File {0} document is invalid", file);
                return false;
            }

            project = doc.Project;
            documents.Add(file, doc);
            return true;
        }

        /// <summary>Adds a list of documents to valid source documents</summary>
        public async Task AddFilesAsync(IEnumerable<string> files)
        {
            //Task.WhenAll(files.Select(file => AddFileAsync(file)));
            foreach (var file in files)
                await AddFileAsync(file);
        }
        

        /// <summary>Perofrms a deep search and finds all the dependencies for this file</summary>
        public async Task<IReadOnlyCollection<string>> FindAllDependenciesAsync(IEnumerable<string> files)
        {
            Logger.Info("Finding Dependencies");

            HashSet<string> results = new HashSet<string>();
            Queue<string> queue = new Queue<string>();
            foreach (var item in files)
            {
                if (results.Add(item))
                    queue.Enqueue(item);
            }

            // While we have a queue, push the file if we can
            while (queue.TryDequeue(out var currentFile))
            {
                Logger.Trace("Searching {0}", currentFile);
                var dependencies = await FindFileDependenciesAsync(currentFile);
                foreach (var dependency in dependencies)
                {
                    Logger.Trace(" - Found {0}", dependency);
                    if (results.Add(dependency))
                        queue.Enqueue(dependency);
                }
            }

            return results;
        }

        /// <summary>Finds the shallow list of dependencies</summary>
        public async Task<string[]> FindFileDependenciesAsync(string file)
        {
            if (!documents.ContainsKey(file))
                await AddFileAsync(file);

            if (dependencyCache.Count == 0)
                await BuidDependencyMap();

            if (dependencyCache.TryGetValue(file, out var deps))
                return deps;

            return new string[0];
        }

        /// <summary>Builds the internal dependency map</summary>
        public async Task BuidDependencyMap()
        {
            Logger.Info("Building Dependency Map");
            Stopwatch stopwatch = Stopwatch.StartNew();
            ConcurrentDictionary<string, ConcurrentBag<string>> mapping = new ConcurrentDictionary<string, ConcurrentBag<string>>();
            await ParallelForEachAsync(project.Documents, (document) => FindSourceDependents(document, mapping), 16);
            dependencyCache = mapping.ToDictionary((kp) => kp.Key, (kp) => kp.Value.Distinct().ToArray());
            
            Logger.Trace("Finished building map. Took {0}ms", stopwatch.ElapsedMilliseconds);
        }

        private async Task FindSourceDependents(Document sourceDocument, ConcurrentDictionary<string, ConcurrentBag<string>> mapping)
        {
            string sourceFile = sourceDocument.FilePath ?? sourceDocument.Name;
            Logger.Trace("Scanning references of {0}", sourceFile);

            var model = await sourceDocument.GetSemanticModelAsync();
            var root = await sourceDocument.GetSyntaxRootAsync();

            foreach (var syntax in root.DescendantNodes().Where(node => node is TypeDeclarationSyntax || node is EnumDeclarationSyntax))
            {
                var symbol = (INamedTypeSymbol)model.GetDeclaredSymbol(syntax);
                if (symbol == null) return;

                //////// IMPORTANT SOLUTION IS IMMUTABLE IT NEEDS TO BE PROJECT.SOLUTION
                var references = await SymbolFinder.FindReferencesAsync(symbol, project.Solution);
                foreach (var reference in references)
                {
                    foreach (var location in reference.Locations)
                    {
                        string refFile = location.Document.FilePath ?? location.Document.Name;
                        if (!mapping.ContainsKey(refFile))
                            mapping.TryAdd(refFile, new ConcurrentBag<string>());
                        mapping[refFile].Add(sourceFile);
                    }
                }
            }
        }

        private static Task ParallelForEachAsync<T>(IEnumerable<T> source, Func<T, Task> funcBody, int maxDoP = 4)
        {
            async Task AwaitPartition(IEnumerator<T> partition)
            {
                using (partition)
                {
                    while (partition.MoveNext())
                    {
                        await Task.Yield(); // prevents a sync/hot thread hangup
                        await funcBody(partition.Current);
                    }
                }
            }

            return Task.WhenAll(
                Partitioner
                    .Create(source)
                    .GetPartitions(maxDoP)
                    .AsParallel()
                    .Select(p => AwaitPartition(p)));
        }

        public void Dispose()
        {
            workspace.Dispose();
        }
    }
}
