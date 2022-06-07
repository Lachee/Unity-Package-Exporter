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
    class Packer : IDisposable, IAsyncDisposable
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetLogger("Packer");

        /// <summary>Path to the Unity Project</summary>
        public string ProjectPath { get; }
        /// <summary>Output file path. If a stream is given, this is null.</summary>
        public string OutputPath { get; }

        private Stream _outStream;
        private GZipOutputStream _gzStream;
        private TarOutputStream _tarStream;

        private HashSet<string> _files;
        public IReadOnlyCollection<string> Files => _files;

        /// <summary>
        /// Creates a new Packer that writes to the output file
        /// </summary>
        /// <param name="projectPath">Path to the Unity Project</param>
        /// <param name="output">The .unitypackage file</param>
        public Packer(string projectPath, string output) : this(projectPath, new FileStream(output, FileMode.OpenOrCreate)) 
        {
            OutputPath = output;
        }

        /// <summary>
        /// Creates a new Packer that writes to the outputStream
        /// </summary>
        /// <param name="projectPath">Path to the Unity Project</param>
        /// <param name="stream">The stream the contents will be written to</param>
        public Packer(string projectPath, Stream stream)
        {
            ProjectPath = projectPath;
            OutputPath = null;

            _files = new HashSet<string>();
            _outStream = stream;
            _gzStream = new GZipOutputStream(_outStream);
            _tarStream = new TarOutputStream(_gzStream);

        }

        /// <summary>
        /// Adds an asset to the pack.
        /// <para>If the asset is already in the pack, then it will be skipped.</para>
        /// </summary>
        /// <param name="filePath">The full path to the asset</param>
        /// <returns>If the asset was written to the pack. </returns>
        public async Task<bool> AddAssetAsync(string filePath)
        {
            FileInfo file = new FileInfo(Path.GetExtension(filePath) == ".meta" ? filePath.Substring(0, filePath.Length - 5) : filePath); 
            if (!file.Exists) throw new FileNotFoundException();
            if (!_files.Add(file.FullName)) return false;

            string relativePath = Path.GetRelativePath(ProjectPath, file.FullName);
            string metaFile = $"{file.FullName}.meta";
            string guidString = "";
            string metaContents;

            if (!File.Exists(metaFile))
            {
                //Meta file is missing so we have to generate it ourselves.
                Logger.Warn("Missing .meta for {0}", relativePath);

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

            Logger.Info("Writing File {0} ( {1} )", relativePath, guidString);
            await _tarStream.WriteFileAsync(file.FullName, $"{guidString}/asset");
            await _tarStream.WriteAllTextAsync($"{guidString}/asset.meta", metaContents);
            await _tarStream.WriteAllTextAsync($"{guidString}/pathname", relativePath.Replace('\\', '/'));
            return true;
        }

        /// <summary>
        /// Adds assets to the pack
        /// <para>If an asset is already in the pack then it will be skipped</para>
        /// </summary>
        /// <param name="assets"></param>
        /// <returns></returns>
        public async Task AddAssetsAsync(IEnumerable<string> assets)
        {
            foreach(var asset in assets)
                await AddAssetAsync(asset);
        }

        public Task FlushAsync()
            => _tarStream.FlushAsync();

        public void Dispose()
        {
            _tarStream.Dispose();
            _gzStream.Dispose();
            _outStream.Dispose();
        }

        public async ValueTask DisposeAsync()
        {
            await _tarStream.DisposeAsync();
            await _gzStream.DisposeAsync();
            await _outStream.DisposeAsync();
        }

        /// <summary>Unpacks all the assets</summary>
        public static Task<IEnumerable<PackageEntry>> Unpack(string package)
        {
            using var fileStream = new FileStream(package, FileMode.Open);
            return Unpack(fileStream);
        }

        /// <summary>Unpacks all assets</summary>
        public async static Task<IEnumerable<PackageEntry>> Unpack(Stream package)
        {
            using var gzoStream = new GZipInputStream(package);
            using var tarStream = new TarInputStream(gzoStream);

            Dictionary<string, PackageEntry> entries = new Dictionary<string, PackageEntry>();

            TarEntry tarEntry;
            while ((tarEntry = tarStream.GetNextEntry()) != null)
            {
                if (tarEntry.IsDirectory)
                    continue;

                string[] parts = tarEntry.Name.Split('/');
                string file = parts[1];
                string guid = parts[0];
                byte[] data = null;

                //Create a new memory stream and read the entries into it.
                using (MemoryStream mem = new MemoryStream())
                {
                    await tarStream.ReadNextFileAsync(mem);
                    data = mem.ToArray();
                }

                //Make sure we actually read data
                if (data == null)
                    continue;

                //Add a new element
                if (!entries.ContainsKey(guid))
                    entries.Add(guid, new PackageEntry());

                switch (file)
                {
                    case "asset":
                        entries[guid].Content = data;
                        break;

                    case "asset.meta":
                        entries[guid].Metadata = data;
                        break;

                    case "pathname":
                        string path = Encoding.ASCII.GetString(data);
                        entries[guid].RelativeFilePath = path;
                        break;

                    default:
                        Logger.Warn("Skipping {0} because its a unkown file", tarEntry.Name);
                        break;
                }
            }

            return entries.Values;
        }
    }
}
