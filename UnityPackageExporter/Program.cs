using ICSharpCode.SharpZipLib.GZip;
using ICSharpCode.SharpZipLib.Tar;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

//-project "C:\Users\TasGDS\Documents\GitHub\discord-rpc-csharp\Unity Example\\" -dir "Assets\\"
namespace UnityPackageExporter
{
    class Program
    {
        static void Main(string[] args)
        {
            CreatePack(args);
        }

        static void CreatePack(string[] args)
        { 
            Console.WriteLine(">>>> Unity Package Exporter by Lachee");

            string unityProject = null;
            string output = "package.unitypackage";
            
            List<string> unpacks = new List<string>();
            List<string> assets = new List<string>();
            List<string> directories = new List<string>();

            bool allOverride = false;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "-unpack":
                        unpacks.Add(args[++i]);
                        break;

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

            foreach(var pack in unpacks)
            {
                Console.WriteLine("Unpacking unitypackage '{0}'", pack);
                UnpackAssets(pack, unityProject);
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

                    string path = Path.Combine(unityProject, "Assets");
                    Console.WriteLine("Looking '{0}'", path);

                    var files = Directory.GetFiles(path, "*", SearchOption.AllDirectories).Where(f => Path.GetExtension(f) != ".meta");
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