using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using xayrga.asn;
using Be.IO;
using System.IO;
using Newtonsoft.Json;

namespace SEBS
{
    internal class SEBSPacker
    {
        SEBSProjectFile Project;
        BeBinaryWriter output;
        string Folder; 

        public SEBSPacker(Stream SEBMS, SEBSProjectFile prj,string project_folder)
        {
            Project = prj;
            output = new BeBinaryWriter(SEBMS);
            Folder = project_folder;
        }

        public static int padTo(BeBinaryWriter bw, int padding)
        {
            int del = 0;
            while (bw.BaseStream.Position % padding != 0)
            {
                bw.BaseStream.WriteByte(0x00);
                bw.BaseStream.Flush();
                del++;
            }
            return del;
        }

        public static int padToInt(int Addr, int padding)
        {
            var delta = (int)(Addr % padding);
            return (padding - delta);
        }

        private void patchPointer24(long addr, long value)
        {

        }

        public void pack()
        {
            // Write init data 
            var bytes = File.ReadAllBytes($"{Folder}/{Project.InitDataPath}");
            output.Write(bytes);
            padTo(output, 32);
            var ProjectBase = output.BaseStream.Position;

        }

   

    }
}
