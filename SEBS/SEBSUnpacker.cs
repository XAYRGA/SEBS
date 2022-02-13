using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using xayrga.asn;
using Be.IO;
using System.IO;

namespace SEBS
{
    internal class SEBSUnpacker
    {
        SEBSInfoFile InfoFile;
        JASNTable SoundTable;
        BeBinaryReader Reader;

        public SEBSUnpacker(Stream SEBMS, SEBSInfoFile prj, JASNTable Stbl)
        {
            InfoFile = prj;
            SoundTable = Stbl;
            Reader = new BeBinaryReader(SEBMS);
        }




        public void unpack(string to_folder)
        {
            if (!Directory.Exists(to_folder))
                Directory.CreateDirectory(to_folder);
            var pad_data = Reader.ReadBytes(InfoFile.InitalizationSectionBytes);
            File.WriteAllBytes($"{to_folder}/init.dat", pad_data);
            File.WriteAllText($"{to_folder}/sebs.json", ":)");

            for (int i = 0; i < InfoFile.Categories.Length; i++)
            {
                var category = InfoFile.Categories[i];
                var catName = i.ToString();
                JASNCategory asnCategory = null;
                if (SoundTable != null)                
                    asnCategory = SoundTable.categories[category.CategoryIndex];

                for (int b=0; b < SoundTable.categories.Length; b++)
                {
                    var cat = SoundTable.categories[b];
                    Console.WriteLine(cat.name);
                }

                if (asnCategory != null)
                        catName = asnCategory.name;

                if (!Directory.Exists($"{to_folder}/{catName}"))
                    Directory.CreateDirectory($"{to_folder}/{catName}");
                Console.WriteLine(catName);

                unpack_category(category, $"{to_folder}/{catName}", asnCategory);
            }
        }

        private void unpack_category(SEBSCategory cat, string to_folder, JASNCategory aSECategory)
        {
            if (aSECategory != null)
                    cmdarg.assert(cat.JumpTable.SuggestedLength > aSECategory.waves.Length, $"project is not sane: #ASNCategory<#ProjectTableEntry {cat.JumpTable.SuggestedLength} > {aSECategory.waves.Length}");
            Reader.BaseStream.Position = cat.RelocatableDataStart;
            var reloc_data = Reader.ReadBytes(cat.RelocatableDataEnd - cat.RelocatableDataEnd);
            File.WriteAllBytes($"{to_folder}/relocationdata.seb", reloc_data);

            for (int i = 0; i < cat.JumpTable.SuggestedLength; i++) {
                var soundName = i.ToString();
                if (aSECategory != null)
                    soundName = aSECategory.waves[i].name;
                File.WriteAllText($"{to_folder}/{soundName}.ubms", ":)");
            }
        }

    }
}
