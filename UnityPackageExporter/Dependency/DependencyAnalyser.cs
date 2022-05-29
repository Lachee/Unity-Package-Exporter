using Microsoft.Extensions.FileSystemGlobbing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UnityPackageExporter.Dependency
{
    class DependencyAnalyser : IDisposable
    {
        public string ProjectPath { get; }

        private ScriptAnalyser scriptAnalyser;
        private AssetAnalyser assetAnalyser;

        private DependencyAnalyser(string projectPath)
        {
            ProjectPath = projectPath;
            scriptAnalyser = new ScriptAnalyser(projectPath);
            assetAnalyser = new AssetAnalyser(projectPath);
        }

        public static Task<DependencyAnalyser> CreateAsync(string projectPath, IEnumerable<string> excludePatterns)
            => CreateAsync(projectPath, new string[] { "**/*.meta" }, new string[] { "**/*.cs" }, excludePatterns);

        /// <summary>
        /// Creates a new Dependency Analyser
        /// </summary>
        /// <param name="projectPath"></param>
        /// <param name="assetPatterns"></param>
        /// <param name="scriptPatterns"></param>
        /// <param name="excludePatterns"></param>
        /// <returns></returns>
        public static async Task<DependencyAnalyser> CreateAsync(string projectPath, IEnumerable<string> assetPatterns, IEnumerable<string> scriptPatterns, IEnumerable<string> excludePatterns)
        {
            DependencyAnalyser analyser = new DependencyAnalyser(projectPath);

            // Build file maps. We dont build code map unless we need it (we might not).
            Matcher assetMatcher = new Matcher();
            assetMatcher.AddIncludePatterns(assetPatterns);
            assetMatcher.AddExcludePatterns(excludePatterns);
            var assetFiles = assetMatcher.GetResultsInFullPath(analyser.ProjectPath);
            await analyser.assetAnalyser.AddFilesAsync(assetFiles);

            // The asset dependency doesnt need this as it finds its own meta files
            Matcher scriptsMatcher = new Matcher();
            scriptsMatcher.AddIncludePatterns(scriptPatterns);
            scriptsMatcher.AddExcludePatterns(excludePatterns);
            var scriptFiles = scriptsMatcher.GetResultsInFullPath(analyser.ProjectPath);
            await analyser.scriptAnalyser.AddFilesAsync(scriptFiles);

            return analyser;
        }

        /// <summary>
        /// Finds all the dependencies 
        /// </summary>
        /// <param name="files"></param>
        /// <returns></returns>
        public async Task<IReadOnlyCollection<string>> FindDependencies(IEnumerable<string> files)
        {
            // Find a list of all assets that we need
            var assets = (await assetAnalyser.FindAllDependenciesAsync(files)).ToArray();

            // Find all the script assets from this list
            var scripts = await scriptAnalyser.FindAllDependenciesAsync(assets.Where(assetFile => Path.GetExtension(assetFile) == ".cs"));

            // Merge the lists
            HashSet<string> results = new HashSet<string>();
            foreach (var asset in assets) results.Add(asset);
            foreach (var script in scripts) results.Add(script);
            return results;
        }

        public void Dispose()
        {
            scriptAnalyser?.Dispose();
            scriptAnalyser = null;      // Setting to null isn't nessary but doing it to feel better.
            assetAnalyser = null;       // Setting to null isn't nessary but doing it to feel better.
        }
    }
}
