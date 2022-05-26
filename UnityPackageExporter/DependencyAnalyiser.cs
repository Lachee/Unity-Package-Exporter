using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

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


        public void AddFile(string file)
        {
            var name = Path.GetFileName(file);
            var fileContent = File.ReadAllText(file);
            var src = SourceText.From(fileContent);
            var doc = project.AddDocument(name, src, filePath: file);
            project = doc.Project;
        }

        public void FindDependencies(string file)
        {
            // Clear WOrkspace
            workspace.TryApplyChanges(solution);

            /*
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
