using System;
using System.Collections.Generic;
using System.Text;
using System.IO;

namespace UnityPackageExporter
{
    class PackageExtraction
    {
        public string PathName { get; set; }
        public byte[] Asset { get; set; }
        public byte[] Metadata { get; set; }

        public bool IsWriteable() => !string.IsNullOrEmpty(PathName) && Asset?.Length > 0 && Metadata?.Length > 0;

        public void Write(string root)
        {
            string path = Path.Combine(root, PathName);
            string dir = Path.GetDirectoryName(path);
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(path, Asset);
            File.WriteAllBytes(path + ".meta", Asset);
        } 
    }
}
