using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SEBS
{

    internal class SEBSProjectCategory
    {
        public string CategoryName;
        public string CategoryPath;
        public string RelocationDataFile;
        public string[] includes;
    }

    internal class SEBSProjectFile
    {
        public string ProjectName;
        public string ProjectAuthor;
        public float ToolVersion;
        public string InitDataPath;
        public string IncludePath;
        
        public SEBSProjectCategory[] Categories;
        public SEBSInfoFile RebuildData;
    }
}
