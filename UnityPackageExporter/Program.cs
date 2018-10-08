using ICSharpCode.SharpZipLib.GZip;
using ICSharpCode.SharpZipLib.Tar;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace UnityPackageExporter
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine(">>>> Unity Package Exporter by Lachee");

            string unityProject = null;
            string output = "package.unitypackage";

            List<string> assets = new List<string>();
            List<string> directories = new List<string>();

            bool allOverride = false;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "-output":
                        output = args[++i];
                        break;

                    case "-project":
                        unityProject = args[++i];
                        break;

                    case "-asset":
                        assets.Add(args[++i]);
                        break;

                    case "-assets":
                        assets.AddRange(args[++i].Split(','));
                        break;

                    case "-dir":
                        directories.Add(args[++i]);
                        break;

                    case "-dirs":
                        directories.AddRange(args[++i].Split(','));
                        break;

                    case "-a":
                        Console.WriteLine("Overrides Enabled");
                        allOverride = true;
                        break;

                    default:
                        Console.WriteLine("Unkown Argument: {0}", args[i]);
                        break;
                }
            }

            if (string.IsNullOrEmpty(unityProject))
            {
                Console.WriteLine("-project is null or empty!");
                return;
            }

            if (!allOverride && assets.Count == 0 && directories.Count == 0)
            {
                Console.WriteLine("No assets or directories supplied");
                return;
            }

            Console.WriteLine("Packing....");
            var stopwatch = new Stopwatch();
            stopwatch.Start();
            { 
                if (allOverride)
                {
                    Console.WriteLine("Packing All....");
                    string path = Path.Combine(unityProject, "Assets\\");

                    Console.WriteLine("Looking '{0}'", path);
                    var files = Directory.GetFiles(path, "*", SearchOption.AllDirectories);
                    PackAssets(output, unityProject, files);
                }
                else
                {
                    Console.WriteLine("Packing Some....");
                    var files = directories
                        .SelectMany(dir => Directory.GetFiles(Path.Combine(unityProject, dir), "*", SearchOption.AllDirectories))
                        .Union(assets)
                        .Where(f => Path.GetExtension(f) != ".meta");

                    PackAssets(output, unityProject, files);
                }
            }
            stopwatch.Stop();

            Console.WriteLine("Finished packing. Took {0}ms", stopwatch.ElapsedMilliseconds);
        }

        public static void PackAssets(string packageOutput, string unityProjectRoot, IEnumerable<string> assets, bool overwrite = true)
        {
            Console.WriteLine("Packing Project '{0}'", unityProjectRoot);

            //Create all the streams
            using (var fileStream = new FileStream(packageOutput, FileMode.Create))
            {
                using (var gzoStream = new GZipOutputStream(fileStream))
                using (var tarStream = new TarOutputStream(gzoStream))
                {
                    //Go over every asset, adding it
                    foreach(var asset in assets)
                    {
                        PackUnityAsset(tarStream, unityProjectRoot, asset);
                    }
                }
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

            //If the file doesnt have a meta then skip it
            if (!File.Exists(metaFile))
            {
                //Meta file is missing so we have to generate it ourselves.
                Console.WriteLine("META: " + assetFile);

                var builder = new System.Text.StringBuilder();
                builder.Append("fileFormatVersion: 2\n");
                builder.Append("guid: " + new Guid()).Append("\n");
                builder.Append("timeCreated: 1521360783").Append("\n");
                builder.Append("licenseType: Free").Append("\n");
                metaContents = builder.ToString();
                return;
            }
            else
            {
                //Read the meta contents
                metaContents = File.ReadAllText(metaFile);
            }

            //Add the file
            Console.WriteLine("ADD: " + relativePath);
            

            //Get the GUID from a quick substring
            int guidIndex = metaContents.IndexOf("guid: ");
            string guid = metaContents.Substring(guidIndex + 6, 32);

            //Add the asset, meta and pathname.
            tarStream.WriteFile(assetFile, $"{guid}/asset");
            tarStream.WriteAllText($"{guid}/asset.meta", metaContents);
            tarStream.WriteAllText($"{guid}/pathname", relativePath.Replace('\\', '/'));            
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
    }
}

#if DONTDOTHIS
        using (var gzoStream = new GZipOutputStream(outStream))
                {
                    using (var tarArchive = TarArchive.CreateOutputTarArchive(gzoStream))
                    {
                        var assetEntry = TarEntry.CreateEntryFromFile(assetFile);
                        assetEntry.Name = $"{guid}/asset";
                        tarArchive.WriteEntry(assetEntry, true);

                        var metaEntry = TarEntry.CreateEntryFromFile(metaFile);
                        metaEntry.Name = $"{guid}/asset.meta";
                        tarArchive.WriteEntry(metaEntry, true);
                        
                        AddTextFile(tarArchive, $"{guid}/pathname", "test.png");
                    }
                }
#endif