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
    class DependencyAnalyiser : IDisposable
    {
        private AdhocWorkspace workspace;
        private Solution solution;
        private Project project;
        private Dictionary<string, Document> documents = new Dictionary<string, Document>();
        private Dictionary<string, string[]> dependencies = new Dictionary<string, string[]>();

        public DependencyAnalyiser(string rootDirectory)
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
            AddSource("PropertyAttribute.cs", "namespace UnityEngine { public class PropertyAttribute : System.Attribute { } }");
        }

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

        public async Task FindDependenciesAsync(string file)
        {
            if (!documents.ContainsKey(file))
                await AddFileAsync(file);

            if (dependencies.Count == 0)
                await BuildDependencyMap();

            var deps = dependencies[file];
            foreach(var dep in deps)
            {
                Console.WriteLine(dep);
            }
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

        /// <summary>Finds all the dependencies this file needs</summary>
        private async Task ExperimentalFindDependenciesAsync(string file)
        {
            // Clear WOrkspace

            // Find the source document from our list of files
            Document sourceDocument;
            if (!documents.TryGetValue(file, out sourceDocument))
            {
                await AddFileAsync(file);
                sourceDocument = documents[file];
            }

            workspace.TryApplyChanges(solution);

            // var model = await sourceDocument.GetSemanticModelAsync();                                                           //Get the semantic model
            // var root = await sourceDocument.GetSyntaxRootAsync();                                                               //Get the syntax
            // var syntax = root.DescendantNodes().OfType<ClassDeclarationSyntax>().First();                                       //Find the first ClassDeclaration within the syntax
            // var symbol = model.GetDeclaredSymbol(syntax);

            var compilation = await project.GetCompilationAsync();
            var sem = await sourceDocument.GetSemanticModelAsync();
            foreach(var st in compilation.SyntaxTrees)
            {
                
                /*
                 * Gather Our Data
                 */
                var implementsList = new List<object>(); //Method Implementations
                var invocationList = new List<object>(); //Method Invocations
                var inheritsList = new List<object>();//Class Heirarchy
                var classCreatedObjects = new List<object>(); //Objects created by classes
                var methodCreatedObjects = new List<object>();//Objects created by methods

                /*
                 * For Each Class
                 */
                var classDeclarations = st.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>();
                foreach (ClassDeclarationSyntax classDeclaration in classDeclarations)
                {
                    var classSymbol = sem.GetDeclaredSymbol(classDeclaration);
                    var classPath = classSymbol.Name;
                    if (classSymbol.ContainingNamespace != null)
                        classPath = classSymbol.ContainingNamespace.Name + '.' + classSymbol.Name;

                    var classinfo = new Dictionary<string, object>();
                    classinfo["name"] = classPath;
                    classinfo["location"] = classDeclaration.GetLocation().ToString();

                    /*
                     * If this Class is a Subclass, Collet Inheritance Info
                     */
                    if (classDeclaration.BaseList != null)
                    {
                        foreach (SimpleBaseTypeSyntax typ in classDeclaration.BaseList.Types)
                        {
                            var symInfo = sem.GetTypeInfo(typ.Type);

                            var baseClassPath = symInfo.Type.Name;
                            if (symInfo.Type.ContainingNamespace != null)
                                baseClassPath = symInfo.Type.ContainingNamespace.Name + '.' + symInfo.Type.Name;

                            var inheritInfo = new Dictionary<string, object>();
                            inheritInfo["class"] = classPath;
                            inheritInfo["base"] = baseClassPath;

                            inheritsList.Add(inheritInfo);
                        }
                    }


                    /*
                     * For each method within the class
                     */
                    var methods = classDeclaration.SyntaxTree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>();
                    foreach (var method in methods)
                    {
                        var symbol = sem.GetDeclaredSymbol(method);

                        //Collect Method Information
                        var methoddata = new Dictionary<string, object>();
                        methoddata["name"] = symbol.MetadataName;
                        if (symbol.ContainingNamespace != null)
                            methoddata["name"] = symbol.ContainingNamespace.Name + "." + symbol.MetadataName;
                        methoddata["location"] = classDeclaration.GetLocation().ToString();
                        methoddata["class"] = classinfo["name"];

                        implementsList.Add(methoddata);

                        var invocations = method.SyntaxTree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>();

                        //For each invocation within our method, collect information
                        foreach (var invocation in invocations)
                        {
                            var invokedSymbol = sem.GetSymbolInfo(invocation).Symbol;

                            if (invokedSymbol == null)
                                continue;

                            var invocationInfo = new Dictionary<string, object>();
                            invocationInfo["name"] = invokedSymbol.MetadataName;
                            if (symbol.ContainingNamespace != null)
                                invocationInfo["name"] = invokedSymbol.ContainingNamespace.Name + "." + invokedSymbol.MetadataName;
                            if (invokedSymbol.Locations.Length == 1)
                                invocationInfo["location"] = invocation.GetLocation().ToString();
                            invocationInfo["method"] = methoddata["name"];

                            invocationList.Add(invocationInfo);
                        }

                        //For each object creation within our method, collect information
                        var methodCreates = method.SyntaxTree.GetRoot().DescendantNodes().OfType<ObjectCreationExpressionSyntax>();
                        foreach (var creation in methodCreates)
                        {
                            var typeInfo = sem.GetTypeInfo(creation);
                            var createInfo = new Dictionary<string, object>();

                            var typeName = typeInfo.Type.Name;
                            if (typeInfo.Type.ContainingNamespace != null)
                                typeName = typeInfo.Type.ContainingNamespace.Name + "." + typeInfo.Type.Name;

                            createInfo["method"] = methoddata["name"];
                            createInfo["creates"] = typeName;
                            createInfo["location"] = creation.GetLocation().ToString();

                            methodCreatedObjects.Add(createInfo);
                        }
                    }

                    //For each object created within the class, collect information
                    var creates = classDeclaration.SyntaxTree.GetRoot().DescendantNodes().OfType<ObjectCreationExpressionSyntax>();
                    foreach (var creation in creates)
                    {
                        var typeInfo = sem.GetTypeInfo(creation);
                        var createInfo = new Dictionary<string, object>();

                        var typeName = typeInfo.Type.Name;
                        if (typeInfo.Type.ContainingNamespace != null)
                            typeName = typeInfo.Type.ContainingNamespace.Name + "." + typeInfo.Type.Name;

                        createInfo["class"] = classPath;
                        createInfo["creates"] = typeName;
                        createInfo["location"] = creation.GetLocation().ToString();
                        classCreatedObjects.Add(createInfo);
                    }
                }



                Console.WriteLine("E");
            }




            /*
             * ASYNC
            var model = await sourceDocument.GetSemanticModelAsync();                                                           //Get the semantic model
            var root = await sourceDocument.GetSyntaxRootAsync();                                                               //Get the syntax
            var syntax = root.DescendantNodes().OfType<ClassDeclarationSyntax>().First();                                       //Find the first ClassDeclaration within the syntax
            var symbol = model.GetDeclaredSymbol(syntax);                                                                       //Get the symbol based of the class declaration

            var references = await SymbolFinder.FindReferencesAsync(symbol, sourceDocument.Project.Solution);                               //Find references
            return references.SelectMany(s => s.Locations).Select(loc => new AnalyticResult(loc));
            */

            /*
             * SYNC
                //Find the model
                var modelAwait = sourceDocument.GetSemanticModelAsync().ConfigureAwait(false);
                while (!modelAwait.GetAwaiter().IsCompleted) yield return State.FindingModel;
                var model = modelAwait.GetAwaiter().GetResult();

                //Find hte root
                var rootAwait = sourceDocument.GetSyntaxRootAsync().ConfigureAwait(false);
                while (!rootAwait.GetAwaiter().IsCompleted) yield return State.FindingRoot;
                var root = rootAwait.GetAwaiter().GetResult();

                //Find the symbol
                var syntax = root.DescendantNodes().OfType<ClassDeclarationSyntax>().First();
                var symbol = model.GetDeclaredSymbol(syntax);

                //Find the references
                var referencesAwait = SymbolFinder.FindReferencesAsync(symbol, sourceDocument.Project.Solution).ConfigureAwait(false);
                while (!referencesAwait.GetAwaiter().IsCompleted) yield return State.FindingSymbol;
                var references = referencesAwait.GetAwaiter().GetResult();
                results.AddRange(references.SelectMany(s => s.Locations).Select(loc => new AnalyticResult(loc)));
            */
        }

        private void RecursiveSymbolLookup(SemanticModel model, SyntaxNode node, int indentLevel)
        {
            var kind = (SyntaxKind)node.RawKind;
            Console.WriteLine("{0}{1}", new string('-', indentLevel * 4), kind);

            if (kind == SyntaxKind.IdentifierName)
            {
                var symbolInfo = model.GetSymbolInfo(node);
                if (symbolInfo.Symbol != null)
                    Console.WriteLine("{0}{1}", new string('|', indentLevel * 4), symbolInfo.Symbol.ToDisplayString());
            }



            foreach (var child in node.DescendantNodes())
                RecursiveSymbolLookup(model, child, indentLevel + 1);
        }

        public void Dispose()
        {
            workspace.Dispose();
        }

    }
}
