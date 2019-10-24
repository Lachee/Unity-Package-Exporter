using ICSharpCode.SharpZipLib.GZip;
using ICSharpCode.SharpZipLib.Tar;
using System;
using System.IO;
using System.Text;
using UnityPackage.Extension;

namespace UnityPackage
{
    public class Package
    {
        public byte[] Pack()
        {
            using (var memoryStream = new MemoryStream())
            {
                using (var gzoStream = new GZipOutputStream(memoryStream))
                using (var tarStream = new TarOutputStream(gzoStream))
                {

                }
                    
                return memoryStream.ToArray();
            }
        }

        /// <summary>
        /// Packs a single asset into the stream.
        /// </summary>
        /// <param name="stream">The stream to pack the asset into</param>
        /// <param name="root">The root Unity project.</param>
        /// <param name="asset">The asset file</param>
        /// <returns></returns>
        private bool PackAsset(TarOutputStream stream, string root, string asset)
        {
            //Skip meta files
            if (Path.GetExtension(asset).ToLowerInvariant() == ".meta")
                return false;

            //Skip missing file
            if (!File.Exists(asset))
                return false;

            string meta = $"{asset}.meta";
            string metaContent = null;
            string guidString = null;

            //Get the GUID
            if (!File.Exists(meta))
            {
                StringBuilder guidBuilder = new StringBuilder();

                //Manually create a GUI string
                Guid guid = Guid.NewGuid();
                foreach (var byt in guid.ToByteArray())
                    guidBuilder.Append(string.Format("{0:X2}", byt));

                guidString = guidBuilder.ToString();
                metaContent = $"guid: {guidString}\n";
            }
            else
            {
                metaContent = File.ReadAllText(meta);
                guidString = metaContent.Substring(metaContent.IndexOf("guid: ") + 6, 32);
            }

            //Write to the stream
            stream.WriteFile(asset, $"{guidString}/asset");
            stream.WriteAllText($"{guidString}/asset.meta", metaContents);
            stream.WriteAllText($"{guidString}/pathname", relativePath.Replace('\\', '/'));

            return true;
        }
    }
}
