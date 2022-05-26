using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace UnityPackageExporter
{
    class DependencyAnalyiser : IDisposable
    {
        private AdhocWorkspace workspace;
        private Solution solution;
        private Project project;

        public DependencyAnalyiser()
        {
            workspace = new AdhocWorkspace();


            //Prepare solution
            var solId = SolutionId.CreateNewId();
            var solutionInfo = SolutionInfo.Create(solId, VersionStamp.Default);
            solution = workspace.AddSolution(solutionInfo);


            //Prepare the project
            var projectInfo = ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Default, "Assembly-CSharp", "Assembly-CSharp", "C#", projectPath);
            project = workspace.AddProject(projectInfo);
        }

        /// <summary>Adds a file</summary>
        public void AddFile(string file)
        {
            var name = Path.GetFileName(file);
            var fileContent = File.ReadAllText(file);
            var src = SourceText.From(fileContent);
            var doc = project.AddDocument(name, src, filePath: file);
            project = doc.Project;
        }

        /// <summary>Adds a file</summary>
        public async Task AddFileAsync(string file)
        {
            var name = Path.GetFileName(file);
            var fileContent = await File.ReadAllTextAsync(file);
            var src = SourceText.From(fileContent);
            var doc = project.AddDocument(name, src, filePath: file);
            project = doc.Project;
        }

        public async Task FindDependenciesAsync(string file)
        {
            // Clear WOrkspace
            workspace.TryApplyChanges(solution);
            await Task.CompletedTask;

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


        public void Dispose()
        {
            workspace.Dispose();
        }

    }
}
