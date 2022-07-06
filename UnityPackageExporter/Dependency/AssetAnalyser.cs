using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityPackageExporter.Dependency;

namespace UnityPackageExporter.Dependency
{
    /// <summary>Analyses Assets for their dependencies</summary>
    class AssetAnalyser
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetLogger("AssetAnalyser");

        /// <summary>Map of AssetID -> FileName</summary>
        private Dictionary<AssetID, FileInfo> fileIndex = new Dictionary<AssetID, FileInfo>();

        /// <summary>Map of GUID -> AssetID</summary>
        private Dictionary<string, AssetID> guidIndex = new Dictionary<string, AssetID>();

        public IReadOnlyDictionary<AssetID, FileInfo> Assets => fileIndex;

        public string ProjectPath { get; }

        public AssetAnalyser(string projectPath)
        {
            ProjectPath = projectPath;
        }

        /// <summary>Adds a file to the list of valid assets to check</summary>
        public async Task AddFileAsync(string file)
        {
            Logger.Trace("Adding file {0}", file);

            string filePath = Path.GetExtension(file) == ".meta" ? file : $"{file}.meta";
            var assetID = await AssetParser.ReadAssetIDAsync(filePath);
            fileIndex[assetID] = new FileInfo(filePath.Substring(0, filePath.Length - 5));
            if (assetID.HasGUID) guidIndex[assetID.guid] = assetID;
        }

        /// <summary>Adds a list of files to the valid assets to check</summary>
        public Task AddFilesAsync(IEnumerable<string> files)
            => Task.WhenAll(files.Select(file => AddFileAsync(file)));

        /// <summary>
        /// Finds the file with the given GUID
        /// </summary>
        /// <param name="guid"></param>
        /// <returns>The FIleInfo, otherwise null if it does not exist</returns>
        public FileInfo FindFile(string guid)
            => fileIndex.Where(kp => kp.Key.guid == guid).Select(kp => kp.Value).FirstOrDefault();
       
        /// <summary>
        /// Finds all assets that depend on the given files
        /// </summary>
        /// <param name="files">The path to the assets to scan for</param>
        /// <param name="deep">Should the search be deep and scan for the reference's references?</param>
        /// <returns>Collection of assets that require this asset</returns>
        public async Task<IReadOnlyCollection<string>> FindAllReferencesAsync(IEnumerable<string> files, bool deep = true)
        {
            Logger.Info("Finding References");

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
                var references = await FindShallowReferencesAsync(currentFile);
                foreach (var reference in references)
                {
                    Logger.Trace(" - Found {0}", reference);
                    if (results.Add(reference) && deep)
                        queue.Enqueue(reference);
                }
            }

            return results;
        }

        /// <summary>
        /// Finds all assets that depend on the asset path
        /// </summary>
        /// <remarks>This is a shallow search and will only get the immediate references. For deep scans, use <see cref="FindAllReferencesAsync(IEnumerable{string})"/></remarks>
        /// <param name="assetPath">The path to the asset to scan</param>
        /// <returns>Collection of assets that require this asset</returns>
        public async Task<IReadOnlyCollection<string>> FindShallowReferencesAsync(string assetPath)
        {

            string filePath = Path.GetExtension(assetPath) == ".meta" ? assetPath : $"{assetPath}.meta";
            var assetID = await AssetParser.ReadAssetIDAsync(filePath);
            if (!assetID.HasGUID) 
                throw new ArgumentException("Asset is not within the list of loaded assets", "assetPath");

            HashSet<string> references = new HashSet<string>();
            foreach(var kp in fileIndex)
            {
                Logger.Trace("Checking {0}", kp.Value.FullName);
                var dependencies = await AssetParser.ParseAssetReferencesAsync(kp.Value.FullName);
                foreach (var dep in dependencies.Where(id => id.guid == assetID.guid))
                    references.Add(kp.Value.FullName);
            }

            return references;
        }

        /// <summary>
        /// Finds all assets that the given files depend on.
        /// </summary>
        /// <remarks>This will not return missing prefabs/assets</remarks>
        /// <param name="files">The assets to find dependencies for.</param>
        /// <param name="deep">Should the search be deep and the dependencies be scanned for dependencies?</param>
        /// <returns>Collection of assets that the given assets require</returns>
        public async Task<IReadOnlyCollection<string>> FindAllDependenciesAsync(IEnumerable<string> files, bool deep = true)
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
                var dependencies = await FindShallowDependenciesAsync(currentFile);
                foreach (var dependency in dependencies)
                {
                    Logger.Trace(" - Found {0}", dependency);
                    if (results.Add(dependency) && deep)
                        queue.Enqueue(dependency);
                }
            }

            return results;
        }

        /// <summary>
        /// Get's a list of files this asset needs. 
        /// <para>This is a shallow search</para>
        /// <remarks>This will not return missing prefabs/assets</remarks>
        /// </summary>
        public async Task<IReadOnlyCollection<string>> FindShallowDependenciesAsync(string assetPath)
        {
            var references = await AssetParser.ParseAssetReferencesAsync(assetPath);
            HashSet<string> files = new HashSet<string>();
            foreach (AssetID reference in references)
            {
                if (TryGetFileFromGUID(reference.guid, out var info))
                {
                    files.Add(info.FullName);
                }
                else
                {
                    Logger.Warn($"Missing Asset: '{reference.guid}'");
                }
            }

            return files;
        }


        /// <summary>
        /// Gets a list of all dependencies from the given files.
        /// </summary>
        /// <param name="files"></param>
        /// <remarks>Unlike file dependencies, this WILL return missing GUIDs, so additional filtering is required.</remarks>
        /// <returns>List of GUIDs in the given files</returns>
        public async Task<IReadOnlyCollection<string>> FindAllGUIDDependenciesAsnyc(IEnumerable<string> files)
        {
            HashSet<string> scannedFiles = new HashSet<string>();
            HashSet<string> scannedGUIDs = new HashSet<string>();
            Queue<string> queue = new Queue<string>();

            // Add pending files
            foreach (var item in files)
            {
                if (scannedFiles.Add(item))
                    queue.Enqueue(item);
            }

            // While we have a queue, push the file if we can
            while (queue.TryDequeue(out var currentFile))
            {
                Logger.Trace("Searching {0}", currentFile);
                var dependencies = await FindGUIDDependenciesAsync(currentFile);
                foreach (var dependency in dependencies)
                {
                    Logger.Trace(" - Found {0}", dependency);
                    if (scannedGUIDs.Add(dependency))
                    {
                        var fileInfo = FindFile(dependency);
                        if (fileInfo != null && scannedFiles.Add(fileInfo.FullName)) 
                            queue.Enqueue(dependency);
                    }
                }
            }

            return scannedGUIDs;
        }

        /// <summary>Get's a list of GUIDs this asset needs.</summary>
        /// <remarks>Unlike file dependencies, this WILL return missing GUIDs, so additional filtering is required.</remarks>
        public async Task<IReadOnlyCollection<string>> FindGUIDDependenciesAsync(string assetPath)
        {
            var references = await AssetParser.ParseAssetReferencesAsync(assetPath);
            return references.Select(r => r.guid).Where(guid => guid != null).ToHashSet();
        }

        private bool TryGetFileFromGUID(string guid, out FileInfo info) {
            if (guid != null && guidIndex.TryGetValue(guid, out var assetID))
            {
                if (fileIndex.TryGetValue(assetID, out var fi))
                {
                    info = fi;
                    return true;
                }
            }
            
            info = null;
            return false;
        }
    }
}
