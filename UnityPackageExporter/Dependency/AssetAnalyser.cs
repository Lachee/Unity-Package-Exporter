using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityPackageExporter.Dependency;

namespace UnityPackageExporter
{
    class AssetAnalyser
    {
        private Dictionary<AssetID, FileInfo> metaLookup = new Dictionary<AssetID, FileInfo>();
        private Dictionary<string, AssetID> guidLookup = new Dictionary<string, AssetID>();

        public string ProjectPath { get; }

        public AssetAnalyser(string projectPath)
        {
            ProjectPath = projectPath;
        }



        /// <summary>
        /// Get's a list of files this asset needs. 
        /// <para>This is a shallow search</para>
        /// </summary>
        public async Task<IReadOnlyCollection<FileInfo>> FindShallowReferencesAsnyc(string assetPath)
        {
            AssetID[] references = await AssetParser.ReadReferencesAsync(assetPath);
            HashSet<FileInfo> files = new HashSet<FileInfo>(references.Length);
            foreach(AssetID reference in references)
            {
                if (TryGetFileFromGUID(reference.guid, out var info))
                    files.Add(info);
            }

            return files;
        }

        /// <summary>Updates the metamap for all files in the project</summary>
        public async Task BuildFileMap()
        {
            List<Task<KeyValuePair<AssetID, string>>> pending = new List<Task<KeyValuePair<AssetID, string>>>();
            foreach (var file in Directory.EnumerateFiles(ProjectPath, "*.meta", SearchOption.AllDirectories))
            {
                pending.Add(AssetParser.ReadAssetIDAsync(file)
                                        .ContinueWith((task) => new KeyValuePair<AssetID, string>(task.Result, file.Substring(0, file.Length - 5))));
            }
            var result = await Task.WhenAll(pending);
            foreach (var kp in result)
            {
                metaLookup[kp.Key] = new FileInfo(kp.Value);
                if (kp.Key.HasGUID)
                    guidLookup[kp.Key.guid] = kp.Key;
            }
        }

        private bool TryGetFileFromGUID(string guid, out FileInfo info) {
            if (guid != null && guidLookup.TryGetValue(guid, out var assetID))
            {
                if (metaLookup.TryGetValue(assetID, out var fi))
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
