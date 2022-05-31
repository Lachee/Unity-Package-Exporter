using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Threading.Tasks;

namespace UnityPackageExporter.Package
{
    /// <summary>
    /// Unity Package asset
    /// </summary>
    class PackageEntry
    {
        /// <summary>Relative path of the asset</summary>
        public string RelativeFilePath { get; set; }
        /// <summary>Content of the asset</summary>
        public byte[] Content { get; set; }
        /// <summary>Content of the metadata</summary>
        public byte[] Metadata { get; set; }

        /// <summary>Is this file writable</summary>
        internal bool IsWriteable
            => !string.IsNullOrEmpty(RelativeFilePath) && Content?.Length > 0 && Metadata?.Length > 0;

        /// <summary>
        /// Writes the entry to file
        /// </summary>
        /// <param name="destination">Root directory the entry will be written relative to.</param>
        /// <returns></returns>
        public async Task WriteFileAsync(string destination)
        {
            // Prepare the folder
            string path = Path.Combine(destination, RelativeFilePath);
            string dir = Path.GetDirectoryName(path);
            Directory.CreateDirectory(dir);

            // Write the contents
            await Task.WhenAll(
                File.WriteAllBytesAsync(path, Content),
                File.WriteAllBytesAsync(path + ".meta", Content)
            );
        }
    }
}
