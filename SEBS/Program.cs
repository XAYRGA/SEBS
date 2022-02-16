using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Be.IO;
using System.IO;
using xayrga.asn;
using Newtonsoft.Json;

namespace SEBS
{
    internal class Program
    {
        // SEBS 
        // Unpacks / Packs SE.BMS file

        // You kiss your dad on the mouth?



        static void Main(string[] args)
        {

            /*
            var addr = 0x000599;
            var BMSFile = File.OpenRead("defaultse.bms"); 
            var BMSReader = new BeBinaryReader(BMSFile);
            var BED = new SEBSBMSDisassembler(BMSReader, "SE", addr);
            BMSEvent bb;
            while ((bb = BED.disassembleNext()) != BMSEvent.FINISH)
                if (bb == BMSEvent.RETURN)
                    break;
            BED.disassembleQueueItems();

            File.WriteAllText("test.txt", BED.output.ToString());
            


             */
            /*
            var inText = File.ReadAllLines("MSD_SE_EV_LITTLE_WAVE_L.txt");
            var outFile = File.OpenWrite("MSD_SE_EV_LITTLE_WAVE_L.bms");
            var outWriter = new BeBinaryWriter(outFile);
            var asm = new SEBSBMSAssembler("MSD_SE_EV_LITTLE_WAVE_L", outWriter, inText);
            asm.fillCommands();
            asm.assembleAll();

            outWriter.Flush();
            outWriter.Close();
            Console.ReadLine();
            */

            ///*
            ///
           /*
            args = new string[]
            {
                "unpack",
                "defaultse.bms",
                "windwaker.json",
                "windwaker",
                "-asnfile",
                "jaiinfo.asn"
            };
            //*/
            /*
             args = new string[]
            {
                "unpack",
                "se.scom",
                "games/sunshine.json",
                "sunshine",
                "-asnfile",
                "mSound.asn"
            };
            //*/
            //*
            args = new string[]
             {
                    "pack",
                    "sunshine",
                    "ss_se.bms",
             };
            //*/
            cmdarg.cmdargs = args;
            var operation = cmdarg.assertArg(0, "Operation");          

            switch (operation)
            {
                case "gentemplate":
                    generateTemplateFile();
                    break;
                case "unpack":
                    {
                        var bmsFile = cmdarg.assertArg(1, "SE.BMS File");
                        var projectFile = cmdarg.assertArg(2, "Game Configuration File");
                        var outputFolder = cmdarg.assertArg(3, "Output Folder");
                        JASNTable ASNTable = null;
                        // loads the nametable.
                        var nameTableFile = cmdarg.findDynamicStringArgument("asnfile", null);
                        if (nameTableFile != null)
                        {
                            cmdarg.assert(!File.Exists(nameTableFile), $"{nameTableFile} doesn't exist or cannot be accessed.");
                            var JASEStm = File.OpenRead(nameTableFile);
                            var JASERead = new BeBinaryReader(JASEStm);
                            ASNTable = JASNTable.readStream(JASERead);

                        
                        }

                        cmdarg.assert(!File.Exists(bmsFile), $"BMSFile {bmsFile} doesn't exist or cannot be accessed. ");
                        cmdarg.assert(!File.Exists(bmsFile), $"Game Configuration {projectFile} doesn't exist or cannot be accessed. ");

                        if (!Directory.Exists(outputFolder))
                            Directory.CreateDirectory(outputFolder);

                        var stm = File.OpenRead(bmsFile);
                        SEBSInfoFile info = null; 
                        try
                        {
                            var dat = File.ReadAllText(projectFile);
                            info = JsonConvert.DeserializeObject<SEBSInfoFile>(dat); 

                        } catch (Exception ex)
                        {
                            cmdarg.assert($"Failed loading Game Configuration {ex.ToString()}");                      
                        }

                        var unpacker = new SEBSUnpacker(stm, info, ASNTable);
                        unpacker.unpack(outputFolder);
                        Console.ReadLine();
                    }
                    break;
                case "pack":
                    {
                        var ProjectFolder = cmdarg.assertArg(1, "Project Folder");
                        var OutputFile = cmdarg.assertArg(2, "Output File");

                        cmdarg.assert(!Directory.Exists(ProjectFolder), $"Project {ProjectFolder} doesn't exist or cannot be accessed. ");
                        cmdarg.assert(!File.Exists($"{ProjectFolder}/sebs.json"), $"Project {ProjectFolder} doesn't contain a sebs.json file.");

                        var stm = File.ReadAllText($"{ProjectFolder}/sebs.json");
                        var outf = File.Open(OutputFile,FileMode.OpenOrCreate,FileAccess.ReadWrite);
                        SEBSProjectFile info = null;
                        try
                        {
                            //var dat = File.ReadAllText(OutputFile);
                            info = JsonConvert.DeserializeObject<SEBSProjectFile>(stm);

                        }
                        catch (Exception ex)
                        {
                            cmdarg.assert($"Failed loading Game Configuration {ex.ToString()}");
                        }

                        var packer = new SEBSPacker(outf, info, ProjectFolder);
                        packer.pack();
                        Console.ReadLine();
                    }
                    break;
                default:
                case "help":
                    help();
                    break;
            }
     
    
        }

        static void help()
        {

        }

        static void generateTemplateFile()
        {
            var beb = new SEBSInfoFile();
            beb.Categories = new SEBSCategory[1];
            beb.Categories[0] = new SEBSCategory();
         
            File.WriteAllText("template_new.json", Newtonsoft.Json.JsonConvert.SerializeObject(beb, Formatting.Indented));
        }
    }
}
