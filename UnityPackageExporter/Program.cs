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
using UnityPackageExporter.Dependency;

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
                getDefaultValue: () => new string[] { "Library/**.*", "**/.*" },
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
                    using DependencyAnalyser analyser = await DependencyAnalyser.CreateAsync(project.FullName, excludePatterns);
                    var additionalAssets = await analyser.FindDependencies(assetMatchResults);

                    foreach (var file in additionalAssets)
                        Console.WriteLine(file);

                    // Pack the assets
                    PackAssets(output.FullName, project.FullName, additionalAssets);
                }

                Console.WriteLine("Finished packing. Took {0}ms", stopwatch.ElapsedMilliseconds);
            }, projectOption, outputOption, assetOption, excludeOption, unpackOption, dependencyOption);
            return command.Invoke(args);
        }



        /// <summary>
        /// Unpacks assets
        /// </summary>
        /// <param name="package"></param>
        /// <param name="unityProjectRoot"></param>
        /// <param name="allowOverride"></param>
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