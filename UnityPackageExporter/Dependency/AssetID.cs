using System;
using System.Collections.Generic;
using System.Text;

namespace UnityPackageExporter.Dependency
{
    struct AssetID
    {
        public string fileID;
        public string guid;

        public bool HasFileID => fileID != null;
        public bool HasGUID => guid != null;
    }
}
