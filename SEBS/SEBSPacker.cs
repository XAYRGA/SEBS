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
        BeBinaryReader input;
        string Folder;
        long baseAddress = 0;
        int dummyAddress = 0;
        Dictionary<int, int> Externals = new Dictionary<int, int>();

        public SEBSPacker(Stream SEBMS, SEBSProjectFile prj, string project_folder)
        {
            Project = prj;
            output = new BeBinaryWriter(SEBMS);
            input = new BeBinaryReader(SEBMS);
            Folder = project_folder;
        }

        public static int padTo(BeBinaryWriter bw, int padding = 32)
        {
            int del = 0;
            while (bw.BaseStream.Position % padding != 0)
            {
                bw.BaseStream.WriteByte(0xFC);
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

        private void relocateSection(int newbase, int original, int limit)
        {
            for (int i = 0; i < limit; i++)
            {
                if (input.BaseStream.Position - newbase > limit)
                    return;
                if (input.BaseStream.Position == input.BaseStream.Length)
                    return;

                var bb = input.ReadByte();


                switch (bb)
                {
                    case 0xE3:
                    case 0xE1:
                    case 0xFC:
                        break;
                    case 0xDA:
                    case 0x80:
                    case 0xCF:
                    case 0xC6:
                        output.BaseStream.Position += 1;
                        break;
                    case 0xA4:
                    case 0xCC:
                    case 0xE7:
                    case 0x98:
                    case 0xA7:
                    case 0xCB:
                        output.BaseStream.Position += 2;
                        break;
                    case 0xEF:
                    case 0xAC:
                    case 0xAD:
                    case 0xAF:
                        output.BaseStream.Position += 3;
                        break;

                    case 0xDF:
                        {
                            input.ReadByte();
                            var odlAddr = input.ReadU24();
                            if (odlAddr < original)
                                throw new Exception("BIG OOPS");
                            output.BaseStream.Position -= 3;
                            output.WriteU24((int)((odlAddr - original) + newbase));
                            break;
                        }
                    case 0xC8:
                        {
                            input.ReadByte();
                            var odlAddr = input.ReadU24();
                            if (odlAddr < original)
                                throw new Exception("BIG OOPS");
                            output.BaseStream.Position -= 3;
                            output.WriteU24((int)((odlAddr - original) + newbase));
                            break;
                        }
                    case 0xC4:
                        {
                            var flags = input.ReadByte();
                            if (flags == 0xC0)
                            {
                                output.BaseStream.Position += 4;
                                break;
                            }
                            var odlAddr = input.ReadU24();
                            if (odlAddr < 1024)        // don't relocate it, it's part of the init section.                    
                                break;                            

                            if (odlAddr < original)  // > 1024, are we making a call to init section?
                                throw new Exception($"BIG OOPS 0x{input.BaseStream.Position:X}");
                            output.BaseStream.Position -= 3;
                            output.WriteU24((int)((odlAddr - original) + newbase));
                            break;
                        }
                    case 0xA9:
                        {
                            var op = input.ReadByte();
                            if ((op & 0x0F) == 0x0C)
                                input.ReadBytes(3);
                            else
                                input.ReadBytes(2);
                            break;
                        }
                    case 0xFB:
                        {
                    
                            var references = 0;
                            char last;
                            while ((last = (char)input.ReadByte()) != 0x00)
                            {
                                if (last == '%')
                                    references++;
                            }

                            for (int x = 0; x < references; x++)
                                input.ReadByte();

                            break;
                        }
                    case 0x00: // ignore padding.
                        break;
                    default:
                        throw new Exception($"Whoops 0x{bb:X} 0x{output.BaseStream.Position:X}");
                }
                output.Flush();
            }
        }

        public void pack()
        {
            // Write init data 
            var bytes = File.ReadAllBytes($"{Folder}/{Project.InitDataPath}");
            output.Write(bytes);
         

            dummyAddress = (int)output.BaseStream.Position;
            
            var lines = File.ReadAllLines($"{Folder}/dummy.txt");
            var assembler = new SEBSBMSAssembler("dummy", output, lines);
            assembler.fillCommands();
            assembler.assembleAll();
         

            for (int i = 0; i < Project.Categories.Length; i++)
                packCategory(Project.Categories[i], Project.RebuildData.Categories[i]);


            padTo(output, 32); // Entire file needs to align to 32 i guess.
            output.Flush();
            output.Close();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Compile successful.");
            Console.ForegroundColor = ConsoleColor.Gray;
        }

        private void packCategory(SEBSProjectCategory cat, SEBSCategory catin)
        {


            Console.WriteLine($"Assembling {cat.CategoryName}");
  
            var catBase = output.BaseStream.Position;
            var sectData = File.ReadAllBytes($"{Folder}/{cat.CategoryPath}/relocationdata.seb");
            output.Write(sectData);
            output.Flush();
        
            var catEnd = output.BaseStream.Position;
            output.BaseStream.Position = catBase;
            relocateSection((int)catBase, catin.RelocatableDataStart, catin.RelocatableDataLength);
            output.Flush();
            output.BaseStream.Position = catEnd;
            output.BaseStream.Position = catBase + catin.JumpTable.InstructionInnerOffset + 3; // seeks past c4 c0
            output.WriteU24((int)catEnd); // patch jump instruction
            output.BaseStream.Position = catEnd;

            //padTo(output,4);
            var pointerTableStart = output.BaseStream.Position;
            for (int i = 0; i < cat.includes.Length; i++)
                output.WriteU24(0);
          

            for (int i = 0; i < cat.includes.Length; i++)
            {

                //Console.WriteLine($"{i}/{cat.includes.Length}");
                var file = cat.includes[i];

                if (file=="(dummy).txt")
                {
                    var oldAddress = output.BaseStream.Position; ;
                    output.BaseStream.Position = pointerTableStart + (3 * i);
                    output.WriteU24((int)dummyAddress);
                    output.BaseStream.Position = oldAddress;
                    Console.WriteLine("dummy...");
                    continue;
                }
                var soundAddress = output.BaseStream.Position;

                output.BaseStream.Position = pointerTableStart + (3 * i);
                output.WriteU24((int)soundAddress);
                output.BaseStream.Position = soundAddress;



         
                var lines = File.ReadAllLines($"{Folder}/{cat.CategoryPath}/{file}");
                var assembler = new SEBSBMSAssembler(file, output, lines);
                assembler.Externals = Externals;
                assembler.fillCommands();
                assembler.DummyAddress = dummyAddress;
                //try
                //{
                assembler.assembleAll();
                //  } catch (Exception ex)
                // {
                //     Console.ForegroundColor = ConsoleColor.Red;
                //     Console.WriteLine($"Compile error: {ex.Message}");
                //     Console.ForegroundColor = ConsoleColor.Gray;
                //     Console.ReadLine();
                // }

                //assembler.dereferenceLabels();
                output.BaseStream.Position = output.BaseStream.Length;
            

            }

            var goBack = output.BaseStream.Position;

            for (int q = 0;  q < catin.OpenInstructions.Length; q++)
            {
                var oi = catin.OpenInstructions[q];
                output.BaseStream.Position = oi.OpenInstructionOffset + 2;
                output.WriteU24((int)catBase);
            }
            output.BaseStream.Position = goBack;

        }



    }

}
   


