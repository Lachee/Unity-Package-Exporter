using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UnityPackageExporter
{
    class ScriptDependencyAnalyser : IDisposable
    {
        private AdhocWorkspace workspace;
        private Solution solution;
        private Project project;
        private Dictionary<string, Document> documents = new Dictionary<string, Document>();
        private Dictionary<string, string[]> dependencies = new Dictionary<string, string[]>();

        public ScriptDependencyAnalyser(string rootDirectory)
        {
            var mscorlib = PortableExecutableReference.CreateFromFile(typeof(object).Assembly.Location);

            workspace = new AdhocWorkspace();
            documents = new Dictionary<string, Document>();

            //Prepare solution
            var solId = SolutionId.CreateNewId();
            var solutionInfo = SolutionInfo.Create(solId, VersionStamp.Default);
            solution = workspace.AddSolution(solutionInfo);

            //Prepare the project
            //var projectInfo = ProjectInfo.Create(ProjectId.CreateNewId(), LanguageNames.CSharp, rootDirectory);
            project = workspace.AddProject("Sample", LanguageNames.CSharp);
            project = project.AddMetadataReference(mscorlib);

            // Suport attributes
            //AddSource("PropertyAttribute.cs", "namespace UnityEngine { public class PropertyAttribute : System.Attribute { } }");
        }

        /// <summary>Adds a source code directly as a reference</summary>
        public void AddSource(string name, string source)
        {
            dependencies.Clear();
            var src = SourceText.From(source);
            var doc = project.AddDocument(name, src);
            project = doc.Project;
            documents.Add(name, doc);
        }

        /// <summary>Adds a file</summary>
        public async Task AddFileAsync(string file)
        {
            dependencies.Clear();
            var name = Path.GetFileName(file);
            var fileContent = await File.ReadAllTextAsync(file);
            var src = SourceText.From(fileContent);
            var doc = project.AddDocument(name, src, filePath: file);
            project = doc.Project;
            documents.Add(file, doc);
        }

        /// <summary>Adds all files</summary>
        public async Task AddFilesAsync(IEnumerable<string> files)
        {
            foreach (var file in files)
                await AddFileAsync(file);
        }

        public async Task<string[]> FindDependenciesAsync(string file)
        {
            if (!documents.ContainsKey(file))
                await AddFileAsync(file);

            if (dependencies.Count == 0)
                await BuildDependencyMap();

            return dependencies[file];
        }

        /// <summary>Builds the internal dependency map</summary>
        private async Task BuildDependencyMap()
        {
            workspace.TryApplyChanges(solution);

            Dictionary<string, HashSet<string>> mapping = new Dictionary<string, HashSet<string>>();
            foreach (var sourceDocument in project.Documents)
            {
                string sourceFile = sourceDocument.FilePath ?? sourceDocument.Name;
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
                                mapping.Add(refFile, new HashSet<string>());
                            mapping[refFile].Add(sourceFile);
                        }
                    }
                }
            }
            dependencies = mapping.ToDictionary((kp) => kp.Key, (kp) => kp.Value.ToArray());
        }

        public void Dispose()
        {
            workspace.Dispose();
        }
    }
}
