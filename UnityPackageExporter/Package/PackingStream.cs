using ICSharpCode.SharpZipLib.GZip;
using ICSharpCode.SharpZipLib.Tar;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UnityPackageExporter.Package
{
    /// <summary>Packs file into a Unity Package</summary>
    class PackingStream : IDisposable, IAsyncDisposable
    {
        public string ProjectPath { get; }
        public string OutputPath { get; }

        private FileStream fileStream;
        private GZipOutputStream gzStream;
        private TarOutputStream tarStream;

        private HashSet<FileInfo> files;

        /// <summary>
        /// Creates a new Packer
        /// </summary>
        /// <param name="output">The .unitypackage file</param>
        public PackingStream(string projectPath, string output)
        {
            ProjectPath = ProjectPath;
            OutputPath = output;

            files = new HashSet<FileInfo>();
            fileStream = new FileStream(output, FileMode.OpenOrCreate);
            gzStream = new GZipOutputStream(fileStream);
            tarStream = new TarOutputStream(gzStream);
        }

        /// <summary>Adds a single asset to the package</summary>
        public async Task<bool> AddAssetAsync(string filePath)
        {
            FileInfo file = new FileInfo(Path.GetExtension(filePath) == ".meta" ? filePath.Substring(0, filePath.Length - 5) : filePath); 
            if (!file.Exists) throw new FileNotFoundException();
            if (!files.Add(file)) return false;

            string relativePath = Path.GetRelativePath(ProjectPath, file.FullName);
            string metaFile = $"{file.FullName}.meta";
            string guidString = "";
            string metaContents = null;

            if (!File.Exists(metaFile))
            {
                //Meta file is missing so we have to generate it ourselves.
                Console.WriteLine("MISSING ASSET FILE: {0}" , file);

                Guid guid = Guid.NewGuid();
                foreach (var byt in guid.ToByteArray())
                    guidString += string.Format("{0:X2}", byt);

                var builder = new System.Text.StringBuilder();
                builder.Append("guid: " + new Guid()).Append("\n");
                metaContents = builder.ToString();
            }
            else
            {
                //Read the meta contents
                metaContents = File.ReadAllText(metaFile);

                int guidIndex = metaContents.IndexOf("guid: ");
                guidString = metaContents.Substring(guidIndex + 6, 32);
            }

            await tarStream.WriteFileAsync(file.FullName, $"{guidString}/asset");
            await tarStream.WriteAllTextAsync($"{guidString}/asset.meta", metaContents);
            await tarStream.WriteAllTextAsync($"{guidString}/pathname", relativePath.Replace('\\', '/'));
            return true;
        }

        /// <summary>Adds all assets to the package</summary>
        public Task AddAssets(IEnumerable<string> assets)
            => Task.WhenAll(assets.Select(asset => AddAssetAsync(asset)));

        public void Dispose()
        {
            tarStream.Dispose();
            gzStream.Dispose();
            fileStream.Dispose();
        }

        public async ValueTask DisposeAsync()
        {
            await tarStream.DisposeAsync();
            await gzStream.DisposeAsync();
            await fileStream.DisposeAsync();
        }
    }
}
