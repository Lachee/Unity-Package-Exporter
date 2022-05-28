using ICSharpCode.SharpZipLib.GZip;
using ICSharpCode.SharpZipLib.Tar;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;
using System;
using System.Collections;
using System.Collections.Generic;
using System.CommandLine;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

//-project "C:\Users\TasGDS\Documents\GitHub\discord-rpc-csharp\Unity Example\\" -dir "Assets\\"
//-project bin/ExampleProject/ -unpack package.unitypackage
namespace UnityPackageExporter
{
    class Program
    {
        static int Main(string[] args) 
        {
            var projectOption = new Option<DirectoryInfo>(new[] { "--project", "--input", "-i" },
                   description: "Project to pack"
            );

            var outputOption = new Option<FileInfo>(new[] { "--output", "-o" },
                    description: "Output package"
            );

            var assetOption = new Option<IEnumerable<string>>(new[] { "--assets", "-a" },
                    getDefaultValue: () => new string[] { "**.*" },
                    description: "Adds an asset to the pack. Supports glob matching."
            );

            var excludeOption = new Option<IEnumerable<string>>(new[] { "--exclude", "-e" },
                getDefaultValue: () => new string[] { },
                description: "Excludes an asset from the pack. Supports glob matching."
            );

            var unpackOption = new Option<IEnumerable<string>>(new[] { "--unpack" },
                 getDefaultValue: () => new string[] { },
                 description: "Unpacks an asset bundle before proceeding. Does not support glob matching."
            );

            var dependencyOption = new Option<bool>(new string[] { "--include-dependencies", "-d" },
                getDefaultValue: () => false,
                description: "Performs dependency analysis"
            );

            var command = new RootCommand
            {
               projectOption,
               outputOption,
               assetOption,
               excludeOption,
               unpackOption,
               dependencyOption
            };

            command.Description = "Exports projects to Unity packages";
            command.SetHandler(async (DirectoryInfo project, FileInfo output, IEnumerable<string> assetPatterns, IEnumerable<string> excludePatterns, IEnumerable<string> unpacks, bool analyiseDependencies) =>
            {

                // Unpack previous packs
                foreach (var pack in unpacks)
                {
                    Console.WriteLine("Unpacking unitypackage '{0}'", pack);
                    UnpackAssets(pack, project.FullName);
                }

                // Make the output file (touch it) so we can exclude
                File.WriteAllBytes(output.FullName, new byte[0]);

                // Match the assets and start packing them
                Console.WriteLine("Packing...");
                var stopwatch = new Stopwatch();
                stopwatch.Start();

                Matcher assetMatcher = new Matcher();
                assetMatcher.AddIncludePatterns(assetPatterns);
                assetMatcher.AddExcludePatterns(excludePatterns);
                assetMatcher.AddExclude(output.Name);
                var assetMatchResults = assetMatcher.GetResultsInFullPath(project.FullName);

                if (!analyiseDependencies)
                {
                    PackAssets(output.FullName, project.FullName, assetMatchResults);
                } 
                else
                {
                    Console.WriteLine("TODO: ANALYSER ONLY CHECKS SCRIPTS FOR SCRIPTS! IT DOESNT READ ASSETS YET");
                    AssetAnalyser assetAnalyser = new AssetAnalyser(project.FullName);
                    await assetAnalyser.BuildFileMap();

                    string testAsset = @"D:\Users\Lachee\Documents\Unity Projects\TargaExperimentHD\Assets\Scenes\Huon.unity";
                    var infos = await assetAnalyser.FindFileDependenciesAsync(testAsset);
                    Console.WriteLine(string.Join("\n", infos.Select(fi => fi.FullName)));

                    // Build a queue of files to analyse
                    Console.WriteLine("Loading Scripts for analysis....");
                    var analyserStopwatch = new Stopwatch();
                    analyserStopwatch.Start();

                    // Initialize the analyser and insert initial files
                    using ScriptAnalyser analyser = new ScriptAnalyser(project.FullName);
                    Matcher depMatcher = new Matcher();
                    depMatcher.AddIncludePatterns(new string[] { "**/*.cs" });
                    assetMatcher.AddExcludePatterns(excludePatterns);
                    
                    // Load up all the script files
                    await analyser.AddFilesAsync(depMatcher.GetResultsInFullPath(project.FullName));

                    // Find all the files from our list of assets
                    var additionalAssets = await analyser.FindAllDependenciesAsync(assetMatchResults);
                    Console.WriteLine("Finished Analysis. Took a total of {0}ms", analyserStopwatch.ElapsedMilliseconds);

                    // Pack the assets
                    PackAssets(output.FullName, project.FullName, additionalAssets, analyser);
                }

                Console.WriteLine("Finished packing. Took {0}ms", stopwatch.ElapsedMilliseconds);
            }, projectOption, outputOption, assetOption, excludeOption, unpackOption, dependencyOption);
            return command.Invoke(args);
        }

        public static void UnpackAssets(string package, string unityProjectRoot, bool allowOverride = true)
        {
            Console.WriteLine("Unpacking Package '{0}' into ", package, unityProjectRoot);

            Dictionary<string, PackageExtraction> assets = new Dictionary<string, PackageExtraction>();

            if (!File.Exists(package))
            {
                Console.WriteLine("ERR: File not found!");
                return;
            }

            using (var fileStream = new FileStream(package, FileMode.Open))
            {
                using (var gzoStream = new GZipInputStream(fileStream))
                using (var tarStream = new TarInputStream(gzoStream))
                {
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
                            tarStream.ReadNextFile(mem);
                            data = mem.ToArray();
                        }

                        //Make sure we actually read data
                        if (data == null)
                        {
                            Console.WriteLine("NDATA: {0}", tarEntry.Name);
                            continue;
                        }

                        //Add a new element
                        if (!assets.ContainsKey(guid))
                            assets.Add(guid, new PackageExtraction());

                        switch(file)
                        {
                            case "asset":
                                assets[guid].Asset = data;
                                break;

                            case "asset.meta":
                                assets[guid].Metadata = data;
                                break;

                            case "pathname":
                                string path = Encoding.ASCII.GetString(data);
                                assets[guid].PathName = path;
                                break;

                            default:
                                Console.WriteLine("SKIP: {0}", tarEntry.Name);
                                break;
                        }

                        if (assets[guid].IsWriteable())
                        {
                            Console.WriteLine("WRITE: {0}", assets[guid].PathName);
                            assets[guid].Write(unityProjectRoot);
                            assets.Remove(guid);
                        }
                    }
                }
            }
        }

        public static void PackAssets(string packageOutput, string unityProjectRoot, IEnumerable<string> assets, ScriptAnalyser analyiser = null)
        {
            Console.WriteLine("Packing Project '{0}'", unityProjectRoot);

            //Create all the streams
            using (var fileStream = new FileStream(packageOutput, FileMode.Create))
            {
                using (var gzoStream = new GZipOutputStream(fileStream))
                using (var tarStream = new TarOutputStream(gzoStream))
                {
                    //Go over every asset, adding it
                    HashSet<string> packedAssets = new HashSet<string>();
                    HashSet<string> additionalAssets = new HashSet<string>();
                    foreach(var asset in assets)
                    {
                        // Pack the asset if we can
                        if (packedAssets.Add(asset))
                        {
                            //PackUnityAsset(tarStream, unityProjectRoot, asset);

                            // Lookup its dependencies if we can
                            if (analyiser != null)
                                AnalyseDependencies(analyiser, asset, additionalAssets, false);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Analyse the asset for all its dependencies
        /// </summary>
        /// <param name="asset"></param>
        /// <param name="dependencies">Referenced list of all dependencies so far</param>
        /// <param name="deep">Deep search for the dependencies' dependencies</param>
        public static void AnalyseDependencies(ScriptAnalyser analyiser, string asset, HashSet<string> dependencies, bool deep = true)
        {
            // Prepare the deep queue
            HashSet<string> deepQueue = new HashSet<string>();

            // Find the deps

            // Process deep queue
            if (deep)
            {
                foreach (var queuedAsset in deepQueue)
                    AnalyseDependencies(analyiser, queuedAsset, dependencies, deep);
            }
        }


        private static void PackUnityAsset(TarOutputStream tarStream, string unityProjectRoot, string assetFile)
        {
            //If the file doesnt exist, skip it
            if (!File.Exists(assetFile))
            {
                Console.WriteLine("SKIP: " + assetFile);
                return;
            }

            //Make sure its not a meta file
            if (Path.GetExtension(assetFile).ToLowerInvariant() == ".meta")
            {
                //Siently skip meta files
                return;
            }

            //Get all the paths
            string relativePath = Path.GetRelativePath(unityProjectRoot, assetFile);
            string metaFile = $"{assetFile}.meta";
            string metaContents = null;
            string guidString = "";

            //If the file doesnt have a meta then skip it
            if (!File.Exists(metaFile))
            {
                //Meta file is missing so we have to generate it ourselves.
                Console.WriteLine("MISSING ASSET FILE: " + assetFile);

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

            //Add the file
            Console.WriteLine("ADD: " + relativePath);
            
            //Add the asset, meta and pathname.
            tarStream.WriteFile(assetFile, $"{guidString}/asset");
            tarStream.WriteAllText($"{guidString}/asset.meta", metaContents);
            tarStream.WriteAllText($"{guidString}/pathname", relativePath.Replace('\\', '/'));            
        }
        
    }

    public static class TarOutputExtensions
    {
        public static void WriteFile(this TarOutputStream stream, string source, string dest)
        {
            using (Stream inputStream = File.OpenRead(source))
            {
                long fileSize = inputStream.Length;

                // Create a tar entry named as appropriate. You can set the name to anything,
                // but avoid names starting with drive or UNC.
                TarEntry entry = TarEntry.CreateTarEntry(dest);

                // Must set size, otherwise TarOutputStream will fail when output exceeds.
                entry.Size = fileSize;

                // Add the entry to the tar stream, before writing the data.
                stream.PutNextEntry(entry);

                // this is copied from TarArchive.WriteEntryCore
                byte[] localBuffer = new byte[32 * 1024];
                while (true)
                {
                    int numRead = inputStream.Read(localBuffer, 0, localBuffer.Length);
                    if (numRead <= 0)
                        break;

                    stream.Write(localBuffer, 0, numRead);
                }

                //Close the entry
                stream.CloseEntry();
            }
        }

        public static void WriteAllText(this TarOutputStream stream, string dest, string content)
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(content);
            
            TarEntry entry = TarEntry.CreateTarEntry(dest);
            entry.Size = bytes.Length;

            // Add the entry to the tar stream, before writing the data.
            stream.PutNextEntry(entry);

            // this is copied from TarArchive.WriteEntryCore
            stream.Write(bytes, 0, bytes.Length);

            //Close the entry
            stream.CloseEntry();
        }

        public static long ReadNextFile(this TarInputStream tarIn, Stream outStream)
        {
            long totalRead = 0;
            byte[] buffer = new byte[4096];
            bool isAscii = true;
            bool cr = false;

            int numRead = tarIn.Read(buffer, 0, buffer.Length);
            int maxCheck = Math.Min(200, numRead);

            totalRead += numRead;

            for (int i = 0; i < maxCheck; i++)
            {
                byte b = buffer[i];
                if (b < 8 || (b > 13 && b < 32) || b == 255)
                {
                    isAscii = false;
                    break;
                }
            }

            while (numRead > 0)
            {
                if (isAscii)
                {
                    // Convert LF without CR to CRLF. Handle CRLF split over buffers.
                    for (int i = 0; i < numRead; i++)
                    {
                        byte b = buffer[i];     // assuming plain Ascii and not UTF-16
                        if (b == 10 && !cr)     // LF without CR
                            outStream.WriteByte(13);
                        cr = (b == 13);

                        outStream.WriteByte(b);
                    }
                }
                else
                    outStream.Write(buffer, 0, numRead);

                numRead = tarIn.Read(buffer, 0, buffer.Length);
                totalRead += numRead;
            }

            return totalRead;
        }
    }
}

#if DONTDOTHIS

        static void GuidShit() { 
            string project = @"D:\Users\Lachee\Documents\C# Projects\2015 Projects\discord-rpc-csharp\Unity Example\";
            string file = @"Assets\Discord RPC\Editor\CharacterLimitAttributeDrawer.cs";
            string path = project + file;

            string contents = File.ReadAllText(path + ".meta");
            int guidindex = contents.IndexOf("guid: ");
            string readGUID = contents.Substring(guidindex + 6, 32);
            Console.WriteLine(readGUID);

            string calcGUID = CalculateGUID(project, file, true);
            Console.WriteLine(calcGUID);

           calcGUID = CalculateGUID(project, file, false);
           Console.WriteLine(calcGUID);
           
           file = file.Remove(0, 7);
           calcGUID = CalculateGUID(project, file, true);
           Console.WriteLine(calcGUID);
           
           calcGUID = CalculateGUID(project, file, false);
           Console.WriteLine(calcGUID);

            Console.WriteLine("Match: {0}", readGUID == calcGUID);
            Console.ReadKey();
        }

        private static string CalculateGUID(string project, string file, bool replace)
        {
            string text = "";
            Guid guid = Guid.Empty;

            FileInfo fi = new FileInfo(project + file);
            string hashable = file; // file;// +  fi.CreationTime;

            //hashable = hashable.ToLowerInvariant();
            if (replace) hashable = hashable.Replace('\\', '/');

            using (MD5 md5 = MD5.Create())
            {
                byte[] hash = md5.ComputeHash(Encoding.ASCII.GetBytes(hashable));
                //hash.Reverse();
                guid = new Guid(hash);
            }


            foreach (var byt in guid.ToByteArray()) text += string.Format("{0:X2}", byt);
            return text.ToLowerInvariant();
        }

#endif