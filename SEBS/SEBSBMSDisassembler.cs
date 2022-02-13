using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Be.IO;

namespace SEBS
{

    public class DebugStringBuilder 
    {
        public void AppendLine(string ass)
        {
            Console.WriteLine(ass);
        }
    }

    internal enum SEBSDisassemblerQueueItemType
    {
        TRACK = 0,
        CALL = 1,
        INTERRUPT = 2,
        ENVELOPE = 3,
    }

    internal class SEBSDissasemblerQueueItem
    {
        public int address;
        public string label;
        public SEBSDisassemblerQueueItemType type;
    }

    internal class SEBSDisasssemblerLabel {
        public string label;
        public bool referenced;
    }
    internal class SEBSBMSDisassembler
    {
        string stackName = "";

        int baseAddress = 0;

        public StringBuilder output = new StringBuilder();

        BeBinaryReader reader;

        Queue<SEBSDissasemblerQueueItem> queueItems = new Queue<SEBSDissasemblerQueueItem>();

        Dictionary<long, SEBSDisasssemblerLabel> refLabels = new Dictionary<long, SEBSDisasssemblerLabel>();

        Dictionary<string, int> labelAccumulator = new Dictionary<string, int>();

        Dictionary<long, int> opcodeLineRelate = new Dictionary<long, int>();   



        public SEBSBMSDisassembler(BeBinaryReader rdr, string stackname, int address)
        {
            reader = rdr;
            stackName = stackname;
            baseAddress = address;
            rdr.BaseStream.Position = address;
            output.AppendLine("##################################################");
            output.AppendLine($"#STACK: {stackname}");
            output.AppendLine("##################################################");
        }
        private string getLabel(string type, int address ,string prm=null)
        {
            if (address < baseAddress)
                return $"$TARGET(0x{address:X})";
            var inc = -1;
            labelAccumulator.TryGetValue(type, out inc);
            inc++;
            labelAccumulator[type] = inc;
            return $"{type}{(prm == null ? "_" : $"_{prm}_")}{inc}";
        }


        public void parseNoteOnEvent(byte note)
        {
            var voiceflags = reader.ReadByte();
            var velocity = reader.ReadByte();

            var voiceID = voiceflags & 0x7;
            var noteOnType = (voiceflags >> 3) & 3;

            if (noteOnType == 0) // noteon
                output.AppendLine($"NOTEON1 {note:X} {voiceflags} {velocity:X}");
            else if (noteOnType == 1) // noteonr
                output.AppendLine($"NOTEON2 {note:X} {voiceflags} {velocity:X} {reader.ReadByte():X} {reader.ReadByte():X}");
            else if (noteOnType == 2) // noteonrd
                output.AppendLine($"NOTEON3 {note:X} {voiceflags} {velocity:X} {reader.ReadByte():X} {reader.ReadByte():X}  {reader.ReadByte():X} ");
            else if (noteOnType == 3)// Noteonrdl 
                output.AppendLine($"NOTEON4 {note:X} {voiceflags} {velocity:X} {reader.ReadByte():X} {reader.ReadByte():X} {reader.ReadByte():X}  {reader.ReadByte():X}");
        }

        public BMSEvent disassembleNext()
        {
            var currentAddress = reader.BaseStream.Position;
            SEBSDisasssemblerLabel labelTag;
            opcodeLineRelate[currentAddress] = output.Length;

            refLabels.TryGetValue(currentAddress, out labelTag);
            if (labelTag != null) 
                if (!labelTag.referenced)
                {
                    labelTag.referenced = true;
                    output.AppendLine($":{labelTag}");
                }


            var opcode = reader.ReadByte();
            BMSEvent ev = BMSEvent.INVALID;
            try {ev = (BMSEvent)opcode;} catch (Exception ex)  
            {
                throw new Exception($"BMS Opcode invalid 0x{opcode:X} at 0x{reader.BaseStream.Position:X}");
            }

            if (opcode < 0x80)
                parseNoteOnEvent(opcode);               
            else if (opcode > 0x80 & opcode < 0x87)
                output.AppendLine($"NOTEOFF {opcode & 0x7}");
            else
                switch (ev)
                {
                    case BMSEvent.CMD_WAIT8:
                        output.AppendLine($"WAIT8 {reader.ReadByte():X}");
                        break;
                    case BMSEvent.CMD_WAIT16:
                        output.AppendLine($"WAIT16 {reader.ReadUInt16():X}");
                        break;
                    case BMSEvent.PARAM_SET_16:
                        {
                            var destinationRegister = reader.ReadByte();
                            // signed.
                            if (destinationRegister == 6) // overflow register for instrument and bank. >> 8 is bank, &8 is instrument.                             
                            {
                                var value = reader.ReadUInt16();
                                output.AppendLine($"SET_BANK_INST {value >> 8:X} {value & 8:X}");
                            }
                            else
                                output.AppendLine($"PARAM_SET_16 {destinationRegister:X} {reader.ReadInt16()}");                            
                        }
                        break;
                    case BMSEvent.OPENTRACK:
                        {
                            var trackID = reader.ReadByte();
                            var trackAddress = (int)reader.ReadU24();
                            var label = getLabel("TRACK", trackAddress, $"OPEN");
                            output.AppendLine($"OPENTRACK {trackID:X} {label}");
                            queueItems.Enqueue(new SEBSDissasemblerQueueItem()
                            {
                                address = trackAddress,
                                label = label,
                                type = SEBSDisassemblerQueueItemType.TRACK
                            });
                        }
                        break;
                    case BMSEvent.JMP:
                        {
                            var flags = reader.ReadByte();
                            var address = reader.ReadU24();
                            var label = getLabel("JUMP", (int)address);
                            if (address > reader.BaseStream.Position)
                                if (!refLabels.ContainsKey(address))
                                    refLabels[address] = new SEBSDisasssemblerLabel()
                                    {
                                        label = label,
                                        referenced = false
                                    };
                                else
                                {
                                    // this lets us go backwards.
                                    var outP = -1;
                                    if (opcodeLineRelate.TryGetValue(address, out outP))
                                        output.Insert(outP, ":" + label);
                                    else
                                        Console.WriteLine("CANNOT FIND BACKLABEL REFERENCE!!!!"); // I should throw. 
                                }
                            output.AppendLine($"JMP {flags:X} {label}");
                        }
                        break;
                    case BMSEvent.CALL:
                        {
                            var flags = reader.ReadByte();
                            var address = reader.ReadU24();
                            var label = getLabel("CALL", (int)address);
                            if (address >= baseAddress) // if its a TARGET we don't need to disassemble it. 
                                queueItems.Enqueue(new SEBSDissasemblerQueueItem()
                                {
                                    address = (int)address,
                                    label = label,
                                    type = SEBSDisassemblerQueueItemType.CALL
                                });
                            output.AppendLine($"CALL {flags:X} {label}");
                        }
                        break;
                    case BMSEvent.SIMPLEENV:
                        {
                            var flags = reader.ReadByte();
                            var address = reader.ReadU24();
                            var label = getLabel("SIMPLEENV", (int)address);
                            if (address >= baseAddress) // if its a TARGET we don't need to disassemble it. 
                                queueItems.Enqueue(new SEBSDissasemblerQueueItem()
                                {
                                    address = (int)address,
                                    label = label,
                                    type = SEBSDisassemblerQueueItemType.ENVELOPE
                                });
                        }
                        break;
                    case BMSEvent.OSCROUTE:
                        output.AppendLine($"OSCROUTE {reader.ReadByte()}");
                        break;
                    case BMSEvent.FLUSHALL:
                        output.AppendLine($"FLUSHALL");
                        break;
                    case BMSEvent.SETLASTNOTE:
                        output.AppendLine($"SETLN {reader.ReadByte()}");
                        break;
                    case BMSEvent.CMD_WAITR:
                        output.AppendLine($"WAITR {reader.ReadByte()}");
                        break;
                    case BMSEvent.LOOP_S:
                        output.AppendLine($"LOOPS {reader.ReadByte()}");
                        break;
                    case BMSEvent.LOOP_E:
                        output.AppendLine($"LOOPE");
                        break;
                    case BMSEvent.SYNCCPU:
                        output.AppendLine($"SNCCPU {reader.ReadUInt16()}");
                        break;
                    case BMSEvent.TEMPO:
                        output.AppendLine($"TEMPO {reader.ReadUInt16()}");
                        break;
                    case BMSEvent.TIMEBASE:
                        output.AppendLine($"TBASE {reader.ReadUInt16()}");
                        break;
                    case BMSEvent.RETURN:
                        output.AppendLine($"RETURN {reader.ReadByte()}");
                        break;
                    case BMSEvent.RETURN_NOARG:
                        output.AppendLine($"RETURN_NOARG {reader.ReadByte()}");
                        break;
                    case BMSEvent.FINISH:
                        output.AppendLine($"FINISH");
                        break;
                    default:
                        throw new Exception($"Unimplemented BMS opcode {ev} (0x{opcode:X}) -> 0x{reader.BaseStream.Position:X}");
                }
            return ev;
        }


        private void parseEnvelope()
        {
            var seekBase = reader.BaseStream.Position;
            for (int i = 0; i < 10; i++)
            {
                var mode = reader.ReadInt16(); 
                var time = reader.ReadInt16();
                var value = reader.ReadInt16();
                output.AppendLine($"#Envelope Frame {i}");
                output.AppendLine($"*MODE {mode}");
                output.AppendLine($"*DURATION {time}");
                output.AppendLine($"*VALUE {value}");
                if (mode > 0xB) 
                    break;                
            }
            output.AppendLine("STOP\r\n#End Envelope");
        }
        public void disassembleQueueItems()
        {
            while (queueItems.Count > 0)
            {
                var qI = queueItems.Dequeue();
                output.AppendLine("");
                output.AppendLine("##################################################");
                output.AppendLine($"#{qI.type}: {qI.label}");
                output.AppendLine("##################################################");
                output.AppendLine($":{qI.label}");

                BMSEvent baby;
                reader.BaseStream.Position = qI.address;
                switch (qI.type)
                {
                    case SEBSDisassemblerQueueItemType.CALL:
                    case SEBSDisassemblerQueueItemType.TRACK:
                        while ((baby = disassembleNext())!=BMSEvent.FINISH)
                            if (baby == BMSEvent.RETURN)
                                break;                      
                        break;
                    case SEBSDisassemblerQueueItemType.ENVELOPE:
                        parseEnvelope();
                        break;
                }
            }
        }
    }
}
