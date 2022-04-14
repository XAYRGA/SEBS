using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using xayrga.asn;
using xayrga;
using Be.IO;
using System.IO;
using Newtonsoft.Json;

namespace SEBS
{
    internal class SEBSUnpacker
    {
        SEBSInfoFile InfoFile;
        JASNTable SoundTable;
        BeBinaryReader Reader;

        Dictionary<long, string> Deduplicator = new Dictionary<long, string>();
        Dictionary<long, string> Externals = new Dictionary<long, string>();
        public SEBSUnpacker(Stream SEBMS, SEBSInfoFile prj, JASNTable Stbl)
        {
            InfoFile = prj;
            SoundTable = Stbl;
            Reader = new BeBinaryReader(SEBMS);
        }

         int globalIncrmeneter = 0; // When you don't have an ASN you'll need unique names. 

        public void unpack(string to_folder)
        {
            if (!Directory.Exists(to_folder))
                Directory.CreateDirectory(to_folder);
            var pad_data = Reader.ReadBytes(InfoFile.InitalizationSectionBytes);
            File.WriteAllBytes($"{to_folder}/init.dat", pad_data);
            File.WriteAllText($"{to_folder}/dummy.txt", "###\r\nRETURN 0\r\n");


            var Proj = new SEBSProjectFile();
            Proj.ProjectName = "SEBS Project";
            Proj.ProjectAuthor = "";
            Proj.ToolVersion = 1.0f;
            Proj.InitDataPath = "init.dat";
            Proj.RebuildData = InfoFile;
            Proj.Categories = new SEBSProjectCategory[InfoFile.Categories.Length];

            for (int i = 0; i < InfoFile.Categories.Length; i++)
            {
                var category = InfoFile.Categories[i];
                var catName = i.ToString();
                JASNCategory asnCategory = null;
                if (SoundTable != null)                
                    asnCategory = SoundTable.categories[category.CategoryIndex];

                if (asnCategory != null)
                        catName = asnCategory.name;

                if (!Directory.Exists($"{to_folder}/{catName}"))
                    Directory.CreateDirectory($"{to_folder}/{catName}");
                Console.WriteLine(catName);

                var cat2 = unpack_category(category, $"{to_folder}/{catName}", asnCategory);
                cat2.CategoryName = catName;
                cat2.CategoryPath = catName;
                Proj.Categories[i] = cat2;
            }
            File.WriteAllText($"{to_folder}/sebs.json", JsonConvert.SerializeObject(Proj, Formatting.Indented));
        }


        private uint[] readJumptable()
        {
            Queue<uint> addrtable = new Queue<uint>();
            while (true)
            {
                var address = Reader.ReadU24();
                if ((address >> 16) > 0x20) // This is arbitrary.  But i think if the SE.BMS is > 2MB , or you know, 1/12 of the gamecube's RAM that it should be invalid. 
                    break;
                addrtable.Enqueue(address);
            }
            var ret = new uint[addrtable.Count];
            var i = 0;
            while (addrtable.Count > 0)
            {
                ret[i] = addrtable.Dequeue();
                i++;
            }
            return ret;
        }

        private SEBSProjectCategory unpack_category(SEBSCategory cat, string to_folder, JASNCategory aSECategory)
        {
            var SECat = new SEBSProjectCategory();
            if (aSECategory != null)
                    cmdarg.assert(cat.JumpTable.SuggestedLength > aSECategory.waves.Length, $"project is not sane: #ASNCategory<#ProjectTableEntry {cat.JumpTable.SuggestedLength} > {aSECategory.waves.Length}");
            Reader.BaseStream.Position = cat.RelocatableDataStart;


            
            Console.WriteLine($"{aSECategory.waves.Length} {cat.JumpTable.SuggestedLength}");
            
           
            SECat.includes = new string[cat.JumpTable.SuggestedLength];
            var reloc_data = Reader.ReadBytes(cat.RelocatableDataLength);
            File.WriteAllBytes($"{to_folder}/relocationdata.seb", reloc_data);
            SECat.RelocationDataFile = "relocationdata.seb";

    
            Reader.BaseStream.Position = cat.JumpTable.InstructionInnerOffset + cat.RelocatableDataStart;
            var command = Reader.ReadByte();
            Console.WriteLine($"{command:X}");
            var flags = Reader.ReadByte();
            var register = Reader.ReadByte();
            Reader.BaseStream.Position = Reader.ReadU24();
            Console.WriteLine($"{Reader.BaseStream.Position:X}");
            var anch = Reader.BaseStream.Position;
            var bbq = readJumptable();
            Console.WriteLine($"ASE Size {aSECategory.waves.Length} CFG Size {cat.JumpTable.SuggestedLength} RJT size {bbq.Length}");

            Reader.BaseStream.Position = anch;

            var DisableImplicit = cmdarg.findDynamicFlagArgument("noimplicitjumpstop");
            for (int i = 0; i < cat.JumpTable.SuggestedLength; i++) {
                var soundName = $"{cat.CategoryIndex}_{i}" ;
                if (aSECategory != null)
                    soundName = aSECategory.waves[i].name;

                SECat.includes[i] = $"{soundName}.txt";
                var Address = Reader.ReadU24();
                var SoundID = $"{ (((cat.CategoryIndex & 0xF) << 12) | 0x800) + i }";
                if (Deduplicator.ContainsKey(Address))
                {
                    var str = "";
                    str+= "##################################################\r\n";
                    str+= $"#STACK: {soundName}\r\n";
                    str+= $"##################################################\r\n\r\n$REFERENCE {Deduplicator[Address]}\r\n";
                    str += $"#Sound ID: 0x{SoundID:X}";
                    File.WriteAllText($"{to_folder}/{soundName}.txt", str);
                    continue;
                }
                Deduplicator[Address] = soundName;
                
                
                //Console.WriteLine($"\t{Address:X}");
                var anchor = Reader.BaseStream.Position;
                var BED = new SEBSBMSDisassembler(Reader, soundName, (int)Address, cat.DummyAddress);
                BED.AllowImplicitCallTermination = !DisableImplicit;
                BED.AddPlateComment($"Sound ID: 0x{SoundID}");
                BMSEvent bb;
                while ((bb = BED.disassembleNext()) != BMSEvent.FINISH)
                    if (bb == BMSEvent.RETURN || bb == BMSEvent.REQUEST_STOP)
                        break;
                BED.disassembleQueueItems();
                BED.fixBrokenLabels();
                Reader.BaseStream.Position = anchor;
                File.WriteAllText($"{to_folder}/{soundName}.txt", BED.output.ToString());
        
            }
            return SECat;
        }

    }
}
