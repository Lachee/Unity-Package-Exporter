using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace UnityPackageExporter.Dependency
{
    /// <summary>Parses unity .meta files</summary>
    class AssetParser
    {
        static readonly Regex Pattern = new Regex(@"(([fF]ileID|guid): ([\-a-z0-9]+))", RegexOptions.Compiled);

        /// <summary>Pending Results</summary>
        private class PendingReference
        {
            public AssetID ID;
            public int startPosition;
            public int endPosition =>
                ID.HasFileID ? 8 + startPosition + ID.fileID.Length : 6 + startPosition + ID.guid.Length;
        }

        /// <summary>Reads an asset's ID</summary>
        public static async Task<AssetID> ReadAssetIDAsync(string assetFilePath)
        {
            // Get the meta filepath
            string filePath = Path.GetExtension(assetFilePath) == ".meta" ? assetFilePath : $"{assetFilePath}.meta";

            // Read the file path
            string content = await File.ReadAllTextAsync(filePath);

            AssetID ID = new AssetID();
            foreach (Match match in Pattern.Matches(content))
            {
                if (match.Groups[2].Value == "guid")
                    ID.guid = match.Groups[3].Value;
                else
                    ID.fileID = match.Groups[3].Value;
            }


            return ID;
        }

        /// <summary>Pulls a list of FileID and GUIDs used by this file</summary>
        public static async Task<AssetID[]> ReadReferencesAsync(string assetFilePath)
        {
            // Validate it is a correct reference
            string ext = Path.GetExtension(assetFilePath);
            switch(ext)
            {
                default:
                    return new AssetID[0];
                
                // Valid assets we can parse for references
                case ".mat":
                case ".prefab":
                case ".unity":
                case ".asset":
                    break;
            }

            List<PendingReference> references = new List<PendingReference>();
            
            string content = await File.ReadAllTextAsync(assetFilePath);
            foreach(Match match in Pattern.Matches(content))
            {
                // Create a ref
                bool addReference = true;
                PendingReference reference = new PendingReference()
                {
                    startPosition = match.Index
                }; 

                // If we are close enough to the previous one, use it instead
                if (references.Count > 1) {
                    int maxPosition = references[references.Count - 1].endPosition;
                    int diff = match.Index - maxPosition;
                    if (diff <= 3)
                    {
                        reference = references[references.Count - 1];
                        addReference = false;
                    }
                }

                // Update the fields
                if (match.Groups[2].Value == "fileID")
                    reference.ID.fileID = match.Groups[3].Value;
                else
                    reference.ID.guid = match.Groups[3].Value;

                // Add the reference if it needs it
                if (addReference)
                    references.Add(reference);
            }

            return references.Select(pr => pr.ID).ToArray();
        }
    }
}
