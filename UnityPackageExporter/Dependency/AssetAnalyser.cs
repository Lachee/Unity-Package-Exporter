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

        private Dictionary<AssetID, FileInfo> fileIndex = new Dictionary<AssetID, FileInfo>();
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
        /// Finds the GUID for the given File
        /// </summary>
        /// <param name="file"></param>
        /// <returns>Finds the GUID. If none is available then it will return null</returns>
        public string FindGUID(string file)
        {
            if (guidIndex.TryGetValue(file, out var assetID))
                return assetID.guid;
            return null;
        }

        /// <summary>
        /// Gets a list of all dependencies for the given list of files
        /// </summary>
        /// <remarks>This will not return missing prefabs/assets</remarks>
        /// <param name="files"></param>
        /// <returns></returns>
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

        /// <summary>
        /// Get's a list of files this asset needs. 
        /// <para>This is a shallow search</para>
        /// <remarks>This will not return missing prefabs/assets</remarks>
        /// </summary>
        public async Task<IReadOnlyCollection<string>> FindFileDependenciesAsync(string assetPath)
        {
            AssetID[] references = await AssetParser.ReadReferencesAsync(assetPath);
            HashSet<string> files = new HashSet<string>(references.Length);
            foreach(AssetID reference in references)
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

        /// <summary>Get's a list of GUIDs this asset needs.</summary>
        /// <remarks>Unlike file dependencies, this WILL return missing GUIDs, so additional filtering is required.</remarks>
        public async Task<IReadOnlyCollection<string>> FindGUIDDependenciesAsync(string assetPath)
        {
            AssetID[] references = await AssetParser.ReadReferencesAsync(assetPath);
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
