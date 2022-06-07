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
        public string RootPath { get; }

        private ScriptAnalyser scriptAnalyser;
        private AssetAnalyser assetAnalyser;

        private DependencyAnalyser(string rootPath)
        {
            RootPath = rootPath;
            scriptAnalyser = new ScriptAnalyser(rootPath);
            assetAnalyser = new AssetAnalyser(rootPath);
        }

        /// <summary>
        /// Creates a new Dependency Analyser
        /// </summary>
        /// <param name="rootPath">Root path to look for assets in</param>
        /// <param name="excludePatterns">Patterns to exclude from all results</param>
        /// <returns></returns>
        public static Task<DependencyAnalyser> CreateAsync(string rootPath, IEnumerable<string> excludePatterns)
            => CreateAsync(rootPath, new string[] { "**/*.meta" }, new string[] { "**/*.cs" }, excludePatterns);

        /// <summary>
        /// Creates a new Dependency Analyser
        /// </summary>
        /// <param name="rootPath">Root path to look for assets in</param>
        /// <param name="assetPatterns">Pattern to find assets. Recommended to scan only .meta files</param>
        /// <param name="scriptPatterns">Pattern to find scripts. Recommended to scan only .cs files</param>
        /// <param name="excludePatterns">Patterns to exclude from all results</param>
        /// <returns></returns>
        public static async Task<DependencyAnalyser> CreateAsync(string rootPath, IEnumerable<string> assetPatterns, IEnumerable<string> scriptPatterns, IEnumerable<string> excludePatterns)
        {
            DependencyAnalyser analyser = new DependencyAnalyser(rootPath);

            // Build file maps. We dont build code map unless we need it (we might not).
            Matcher assetMatcher = new Matcher();
            assetMatcher.AddIncludePatterns(assetPatterns);
            assetMatcher.AddExcludePatterns(excludePatterns);
            var assetFiles = assetMatcher.GetResultsInFullPath(analyser.RootPath);
            await analyser.assetAnalyser.AddFilesAsync(assetFiles);

            // The asset dependency doesnt need this as it finds its own meta files
            Matcher scriptsMatcher = new Matcher();
            scriptsMatcher.AddIncludePatterns(scriptPatterns);
            scriptsMatcher.AddExcludePatterns(excludePatterns);
            var scriptFiles = scriptsMatcher.GetResultsInFullPath(analyser.RootPath);
            await analyser.scriptAnalyser.AddFilesAsync(scriptFiles);

            return analyser;
        }

        /// <summary>
        /// Finds all the dependencies 
        /// </summary>
        /// <param name="files"></param>
        /// <returns></returns>
        public async Task<IReadOnlyCollection<string>> FindDependenciesAsync(IEnumerable<string> files)
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
