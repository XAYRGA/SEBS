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
        JUMPTABLE = 4,
        EXTERNAL = 5,
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

        Dictionary<int, string> labelDeduplicator = new Dictionary<int, string>();

        Dictionary<long, bool> callDeduplicator = new Dictionary<long, bool>();

        int DummyAddress = 0;

        public bool AllowImplicitCallTermination = true; 

        public SEBSBMSDisassembler(BeBinaryReader rdr, string stackname, int address, int dummy=0)
        {
            reader = rdr;
            stackName = stackname;
            baseAddress = address;
            rdr.BaseStream.Position = address;
            DummyAddress = dummy;
            output.AppendLine("##################################################");
            output.AppendLine($"#STACK: {stackname}");
            output.AppendLine("##################################################");
        }
        private string getLabel(string type, int address ,string prm=null)
        {
            if (address < 1024)
                return $"${address:X}";
            if (labelDeduplicator.ContainsKey(address))
                return labelDeduplicator[address];
            var inc = -1;
            labelAccumulator.TryGetValue(type, out inc);
            inc++;
            labelAccumulator[type] = inc;
            var lab = $"{type}{(prm == null ? "_" : $"_{prm}_")}{inc}";
            labelDeduplicator[address] = lab;
            return lab;
        }


        public void parseNoteOnEvent(byte note)
        {
            var voiceflags = reader.ReadByte();
            var velocity = reader.ReadByte();

            var voiceID = voiceflags & 0x7;
            var noteOnType = (voiceflags >> 3) & 3;

            if (noteOnType == 0) // noteon
                output.AppendLine($"NOTEON1 h{note:X} h{voiceflags:X} h{velocity:X}");
            else if (noteOnType == 1) // noteonr
                output.AppendLine($"NOTEON2 h{note:X} h{voiceflags:X} h{velocity:X} h{reader.ReadByte():X} h{reader.ReadByte():X}");
            else if (noteOnType == 2) // noteonrd
                output.AppendLine($"NOTEON3 h{note:X} h{voiceflags:X} h{velocity:X} h{reader.ReadByte():X} h{reader.ReadByte():X} h{reader.ReadByte():X}");
            else if (noteOnType == 3)// Noteonrdl 
                output.AppendLine($"NOTEON4 h{note:X} h{voiceflags:X} h{velocity:X} h{reader.ReadByte():X} h{reader.ReadByte():X} h{reader.ReadByte():X} h{reader.ReadByte():X}");
        }

        public BMSEvent disassembleNext(int override_byte=-1)
        {


            var currentAddress = reader.BaseStream.Position;
            SEBSDisasssemblerLabel labelTag;


            refLabels.TryGetValue(currentAddress, out labelTag);
            if (labelTag != null) 
                if (!labelTag.referenced)
                {
                    labelTag.referenced = true;
                    output.AppendLine($":{labelTag.label}");
                }
            opcodeLineRelate[reader.BaseStream.Position] = output.Length;
            if (reader.BaseStream.Position >= reader.BaseStream.Length - 0x10)
            {
                output.AppendLine($"FINISH #Finish because of EOF");
                return BMSEvent.FINISH;
            }

            byte opcode = 0;
            if (override_byte == -1)
                opcode = reader.ReadByte();
            else
                opcode = (byte)override_byte;

            BMSEvent ev = BMSEvent.INVALID;
            try {ev = (BMSEvent)opcode;} catch (Exception ex)  
            {
                throw new Exception($"BMS Opcode invalid 0x{opcode:X} at 0x{reader.BaseStream.Position:X}");
            }


            if (opcode < 0x80)
            {
                parseNoteOnEvent(opcode);
                return BMSEvent.NOTE_ON; // LMAO. Had to do this because of NOTE_ON 3
            }
            else if (opcode > 0x80 & opcode < 0x87)
                output.AppendLine($"NOTEOFF {opcode & 0x7}");
            else
                switch (ev)
                {
                    case BMSEvent.CMD_WAIT8:
                        output.AppendLine($"WAIT8 h{reader.ReadByte():X}");
                        break;
                    case BMSEvent.CMD_WAIT16:
                        output.AppendLine($"WAIT16 h{reader.ReadUInt16():X}");
                        break;
                    case BMSEvent.PARAM_SET_16:
                        {
                            var destinationRegister = reader.ReadByte();
                            // signed.
                            if (destinationRegister == 6) // overflow register for instrument and bank. >> 8 is bank, &8 is instrument.                             
                                output.AppendLine($"SET_BANK_INST {reader.ReadByte()} {reader.ReadByte()}");
                            else
                                output.AppendLine($"PARAM_SET_16 h{destinationRegister:X} {reader.ReadInt16()}");
                        }
                        break;
                    case BMSEvent.OPENTRACK:
                        {
                            var trackID = reader.ReadByte();
                            var address = (int)reader.ReadU24();
                            var label = getLabel("TRACK", address, $"OPEN");
                            output.AppendLine($"OPENTRACK h{trackID:X} {label}");
                            var outP = -1;
                            if (!tryBackseekLabel4Tracks((int)address, label))
                                queueItems.Enqueue(new SEBSDissasemblerQueueItem()
                                {
                                    address = address,
                                    label = label,
                                    type = SEBSDisassemblerQueueItemType.TRACK
                                });
                        }
                        break;
                    case BMSEvent.JMP:
                        {
                            var flags = reader.ReadByte();
                            var address = reader.ReadU24();
                            var label = getLabel("JUMP", (int)address); // should already deduplicate. 


                            if (!tryBackseekLabel((int)address, label))
                            { // tries to see if we've already covered the address, and inserts the label there if we have. 
                               // warn($"backwards label reference for {label} failed. I'll try to resolve automatically..."); // I should throw. 
                                refLabels[address] = new SEBSDisasssemblerLabel()
                                {
                                    label = label,
                                    referenced = false
                                };
                            }
                            
                            output.AppendLine($"JMP h{flags:X} {label}");
                            if (flags == 0 && AllowImplicitCallTermination)
                            {
                                output.AppendLine("# JMP 0: IMPLICIT CALL TERMINATION");
                                return BMSEvent.REQUEST_STOP;
                            }
                            
                        }
                        break;
                    case BMSEvent.CALL:
                        {
                            var flags = reader.ReadByte();

                            if (flags == 0xC0)
                            {
                                var register = reader.ReadByte();
                                var address = reader.ReadU24();
                                var label = getLabel("JUMPTABLE", (int)address);

                                queueItems.Enqueue(new SEBSDissasemblerQueueItem()
                                {
                                    address = (int)address,
                                    label = label,
                                    type = SEBSDisassemblerQueueItemType.JUMPTABLE
                                });
                                output.AppendLine($"CALLTABLE h{flags:X} {label}");
                            }
                            else
                            {
                                var address = reader.ReadU24();

                                var label = getLabel("CALL", (int)address); // already does dedupe. 
                                    if (label[0]!='$')
                                        if (!tryBackseekLabel((int) address, label))
                                            queueItems.Enqueue(new SEBSDissasemblerQueueItem() {
                                                address = (int)address,
                                                label = label,
                                                type = SEBSDisassemblerQueueItemType.CALL
                                            });
                        
                                output.AppendLine($"CALL h{flags:X} {label}");
                            }
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
                            output.AppendLine($"SIMPLEENV h{flags:X} {label}");
                        }
                        break;
                    case BMSEvent.SETINTERRUPT:
                        {
                            var ilevel = reader.ReadByte();
                            var address = reader.ReadU24();
                            var label = getLabel("INTERRUPT", (int)address);
                            if (address >= baseAddress) // if its a TARGET we don't need to disassemble it. 
                                queueItems.Enqueue(new SEBSDissasemblerQueueItem()
                                {
                                    address = (int)address,
                                    label = label,
                                    type = SEBSDisassemblerQueueItemType.CALL
                                });
                            output.AppendLine($"INTERRUPT h{ilevel:X} {label}");
                        }
                        break;
                    case BMSEvent.OPOVERRIDE_4:
                        {
                        
                            var instruction = reader.ReadByte();
                            var mask = reader.ReadByte();
                            output.Append($"OVERRIDE4 h{instruction:X} h{mask:X} ");
                            var @event = disassembleNext(instruction);
                            output.AppendLine($"$WRITEARGS b{reader.ReadByte():X} b{reader.ReadByte():X} b{reader.ReadByte():X} b{reader.ReadByte():X}");
                        }
                        break;
                    case BMSEvent.PRINTF:
                        {
                            var references = 0;
                            var message = "";
                            char last;
                            while ((last = (char)reader.ReadByte())!=0x00)
                            {
                                if (last == '%')
                                    references++;
                                message += last;
                            }
                            output.AppendLine("PRINTF " + message);
                            output.Append("$WRITEARGS ");
                            for (int i=0; i<references; i++) 
                                output.Append($"b{reader.ReadByte():X}");

                            output.AppendLine();
                        }
                        break;
                    case BMSEvent.CLOSETRACK:
                        output.AppendLine($"CLOSETRACK h{reader.ReadByte():X}");
                        break;
                    case BMSEvent.SIMPLEOSC:
                        output.AppendLine($"SIMPLEOSC h{reader.ReadByte():X}");
                        break;
                    case BMSEvent.TRANSPOSE:
                        output.AppendLine($"TRANSPOSE h{reader.ReadByte():X}");
                        break;
                    case BMSEvent.CLRI:
                        output.AppendLine("CLEARINTERRUPT");
                        break;
                    case BMSEvent.RETI:
                        output.AppendLine("RETURNINTERRUPT");
                        break;
                    case BMSEvent.OSCROUTE:
                        output.AppendLine($"OSCROUTE h{reader.ReadByte():X}");
                        break;
                    case BMSEvent.VIBDEPTH:
                        output.AppendLine($"VIBDEPTH h{reader.ReadByte():X}");
                        break;
                    case BMSEvent.VIBDEPTHMIDI:
                        output.AppendLine($"VIBDEPTHMIDI h{reader.ReadByte():X} h{reader.ReadByte():X}");
                        break;
                    case BMSEvent.VIBPITCH:
                        output.AppendLine($"VIBPITCH h{reader.ReadByte():X}");
                        break;
                    case BMSEvent.FLUSHALL:
                        output.AppendLine($"FLUSHALL");
                        break;
                    case BMSEvent.IIRCUTOFF:
                        output.AppendLine($"IIRCUTOFF h{reader.ReadByte():X}");
                        break;
                    case BMSEvent.SIMPLEADSR:
                        output.AppendLine($"SIMPLEADSR {reader.ReadUInt16()} {reader.ReadUInt16()} {reader.ReadUInt16()} {reader.ReadUInt16()} {reader.ReadUInt16()}");
                        break;
                    case BMSEvent.READPORT:
                        output.AppendLine($"READPORT h{reader.ReadByte():X} h{reader.ReadByte():X}");
                        break;
                    case BMSEvent.WRITEPORT:
                        output.AppendLine($"WRITEPORT h{reader.ReadByte():X} h{reader.ReadByte():X}");
                        break;
                    case BMSEvent.CHILDWRITEPORT:
                        output.AppendLine($"CHILDWRITEPORT h{reader.ReadByte():X} h{reader.ReadByte():X}");
                        break;
                    case BMSEvent.PERF_S8_DUR_U16:
                        output.AppendLine($"PERF_S8_DUR_U16 h{reader.ReadByte():X} {reader.ReadSByte()} {reader.ReadUInt16()} ");
                        break;
                    case BMSEvent.PERF_S16_NODUR:
                        output.AppendLine($"PERF_S16 h{reader.ReadByte():X} {reader.ReadUInt16()} ");
                        break;
                    case BMSEvent.PERF_S16_DUR_U8_9E:
                        output.AppendLine($"PERF_S16_U8_9E h{reader.ReadByte():X} {reader.ReadInt16()} {reader.ReadByte():X}");
                        break;
                    case BMSEvent.PERF_S16_DUR_U8:
                        output.AppendLine($"PERF_S16_U8 h{reader.ReadByte():X} {reader.ReadUInt16()} {reader.ReadByte():X}");
                        break;
                    case BMSEvent.PERF_S8_NODUR:
                        output.AppendLine($"PERF_S8 h{reader.ReadByte():X} {reader.ReadSByte()}");
                        break;
                    case BMSEvent.PARAM_SET_R:
                        output.AppendLine($"PAR_SET_REG h{reader.ReadByte():X} h{reader.ReadByte():X}");
                        break;
                    case BMSEvent.PARAM_ADD_R:
                        output.AppendLine($"PAR_ADD_REG h{reader.ReadByte():X} h{reader.ReadByte():X}");
                        break;
                    case BMSEvent.PARAM_BITWISE:
                        {
                            var op = reader.ReadByte();
                            //Console.WriteLine($"{op:X}");
                            if ((op & 0x0F) == 0x0C)
                                output.AppendLine($"PARAM_BITWISE_C h{op:X} h{reader.ReadByte():X} h{reader.ReadByte():X} h{reader.ReadByte():X}");
                            else if ((op & 0x0F) == 0x08)
                                output.AppendLine($"PARAM_BITWISE_8 h{op:X} h{reader.ReadByte():X} h{reader.ReadByte():X}");
                            else
                                output.AppendLine($"PARAM_BITWISE h{op:X} h{reader.ReadByte():X} h{reader.ReadByte():X}");
                        }
                        break;
                    case BMSEvent.PERF_S8_DUR_U8:
                        output.AppendLine($"PERF_S8_U8 h{reader.ReadByte():X} {reader.ReadSByte()} h{reader.ReadByte():X}");
                        break;
                    case BMSEvent.PARAM_SET_8:
                        output.AppendLine($"PAR_SET_8 h{reader.ReadByte():X} h{reader.ReadByte():X}");
                        break;
                    case BMSEvent.PARAM_ADD_8:
                        output.AppendLine($"PAR_ADD_8 h{reader.ReadByte():X} h{reader.ReadByte():X}");
                        break;
                    case BMSEvent.PARAM_ADD_16:
                        output.AppendLine($"PAR_ADD_16 h{reader.ReadByte():X} {reader.ReadInt16()}");
                        break;
                    case BMSEvent.PARAM_CMP_8:
                        output.AppendLine($"PAR_CMP_8 h{reader.ReadByte():X} h{reader.ReadByte():X}");
                        break;
                    case BMSEvent.PARAM_CMP_R:
                        output.AppendLine($"PAR_CMP_R h{reader.ReadByte():X} h{reader.ReadByte():X}");
                        break;
                    case BMSEvent.SETPARAM_90:
                        output.AppendLine($"PAR_SET_90 h{reader.ReadByte():X} h{reader.ReadByte():X}");
                        break;
                    case BMSEvent.SETLASTNOTE:
                        output.AppendLine($"SETLN h{reader.ReadByte():X}");
                        break;
                    case BMSEvent.CMD_WAITR:
                        output.AppendLine($"WAITR h{reader.ReadByte():X}");
                        break;
                    case BMSEvent.LOOP_S:
                        output.AppendLine($"LOOPS h{reader.ReadByte():X}");
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
                        output.AppendLine($"RETURN h{reader.ReadByte():X}");
                        var peek = reader.ReadByte();
                        if (peek == 0xFF)
                            output.AppendLine("FINISH");
                        reader.BaseStream.Position--;
                        break;
                    case BMSEvent.RETURN_NOARG:
                        output.AppendLine($"RETURN_NOARG");
                        break;
                    case BMSEvent.FINISH:
                        output.AppendLine($"FINISH");
                        break;
                    default:
                        throw new Exception($"Unimplemented BMS opcode {ev} (0x{opcode:X}) -> 0x{reader.BaseStream.Position:X}");
                }
           
            return ev;
        }

        public bool tryBackseekLabel(int address, string label)
        {
            var outP = -1;
            string wouldbeLabel = $":{label}\r\n";
            if (opcodeLineRelate.TryGetValue(address, out outP)) {
                output = output.Insert(outP, wouldbeLabel);
                List<long> keys = new List<long>(opcodeLineRelate.Keys); // Things like this make me hate this language, you know. 
                // foreach (KeyValuePair<long, int> b in opcodeLineRelate) Sure. Any normal person would have just looped through each of these and changed their value on the fly
                // But nah, enumerations don't let you modify memory content of the object that you're enumerating.
                // which yknow. I would understand if it was a key that I was modifying, but a value? InvalidOperationException? Come the __FUCK__ on. 
                // There's no excuse for having to extract the keys first THEN looping over it. 
                foreach (long key in keys)
                {                    
                    if (key >= address)
                        opcodeLineRelate[key]+=wouldbeLabel.Length; // hopefully we can ASSUME this is right >.>. 
                }                                                                // this took me a minute to figure out.
                                                                                 // If we're inserting information into the data,
                                                                                 // we have to shift all of the previous offsets forward that are in front of us.
                                                                                 // Otherwise after the first one the rest are invalid.....
                return true;
            }
            return false;
        }

        public bool tryBackseekLabel4Tracks(int address, string label)
        {
            var outP = -1;
            string wouldbeLabel = $"####### !Instructions can fall-through on both sides! \r\n:{label}\r\n#######\r\n";
            if (opcodeLineRelate.TryGetValue(address, out outP))
            {
                output = output.Insert(outP, wouldbeLabel);
                List<long> keys = new List<long>(opcodeLineRelate.Keys); 
                foreach (long key in keys)
                    if (key >= address)
                        opcodeLineRelate[key] += wouldbeLabel.Length;
                return true;
            }
            return false;
        }


        private uint[] readJumptable()
        {
            Queue<uint> addrtable = new Queue<uint>();
            while (true)
            {
                var address = reader.ReadU24();
                if ((address >> 16) > 0x20) // This is arbitrary.  But i think if the SE.BMS is > 2MB , or you know, 1/12 of the gamecube's RAM that it should be invalid. 
                    break;
                addrtable.Enqueue(address);
            }
            var ret = new uint[addrtable.Count];
            var i = 0;
            while(addrtable.Count > 0)
            {
                ret[i] = addrtable.Dequeue();
                i++;
            }
            return ret;
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
                output.AppendLine($"ENV MODE {mode}");
                output.AppendLine($"ENV DURATION {time}");
                output.AppendLine($"ENV VALUE {value}");
                if (mode > 0xB) 
                    break;                
            }
            output.AppendLine("STOP\r\n#End Envelope");
        }

        public void warn(string atm)
        {
            var b = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write("Warning: ");
            Console.ForegroundColor = b;
            Console.WriteLine($" [{stackName}] {atm} ");
        }

        public void crit(string atm)
        {
            var b = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Write("Critical: ");
            Console.ForegroundColor = b;
            Console.WriteLine($" [{stackName}] {atm} ");
        }
        public void fixBrokenLabels()
        {
            var broken = true;
            while (broken == true) {
                broken = false;
                foreach (KeyValuePair<long, SEBSDisasssemblerLabel> bk in refLabels)
                {
             
                    if (bk.Value.referenced == false)
                    {
                        broken = true;
                        warn($"Label {bk.Value.label} without reference!");
                        queueItems.Enqueue(new SEBSDissasemblerQueueItem()
                        {
                            address = (int)bk.Key,
                            label = bk.Value.label,
                            type = SEBSDisassemblerQueueItemType.EXTERNAL
                        });
                    }
                }
                if (broken == true)
                    disassembleQueueItems(true);
                else
                    return;
            }

        }
        public void disassembleQueueItems(bool fixingRefs = false)
        {
            while (queueItems.Count > 0)
            {
                var qI = queueItems.Dequeue();
                if (callDeduplicator.ContainsKey(qI.address)) // dont generate multiple calls
                    continue;
                callDeduplicator[qI.address] = true;

                if (refLabels.ContainsKey(qI.address))
                    refLabels[qI.address].referenced = true; // prevents messy references.

                void generateBanner(bool leadingNewline=false)
                {

                    if (!leadingNewline)
                        output.AppendLine("");
                    output.AppendLine("##################################################");
                    output.AppendLine($"#{qI.type}: {qI.label}");
                    output.AppendLine("##################################################");
                    output.AppendLine($":{qI.label}");
                }

                BMSEvent baby;
                reader.BaseStream.Position = qI.address;
                switch (qI.type)
                {
            
                    case SEBSDisassemblerQueueItemType.TRACK:
                        {                            
                            if (!tryBackseekLabel4Tracks(qI.address,qI.label))
                            {
                                generateBanner();
                                while ((baby = disassembleNext()) != BMSEvent.FINISH)
                                    if (baby == BMSEvent.RETURN || baby== BMSEvent.REQUEST_STOP)
                                        break;
                            }
                            break;
                        }
                    case SEBSDisassemblerQueueItemType.CALL:
                        {
                        
                            generateBanner();
                            while ((baby = disassembleNext()) != BMSEvent.FINISH)
                                if (baby == BMSEvent.RETURN  || baby == BMSEvent.REQUEST_STOP)
                                    break;
                            break;
                        }
                    case SEBSDisassemblerQueueItemType.EXTERNAL:

                        output.AppendLine();


                        output.AppendLine("##################################################");
                        output.AppendLine($"#{qI.type}: {qI.label}");
                        output.AppendLine("##################################################");
                        output.AppendLine($":{qI.label} EXTERNAL h{qI.address}");

                        if (fixingRefs)
                        {
                            var b = Console.ForegroundColor;
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.Write("Notice: ");
                            Console.ForegroundColor = b;
                            Console.WriteLine($" [{stackName}] Resolved EXTERNAL label reference ({qI.label}) ");
                        }
           
                        while ((baby = disassembleNext()) != BMSEvent.FINISH)
                            if (baby == BMSEvent.RETURN || baby == BMSEvent.REQUEST_STOP)
                                break;

                        output.AppendLine($"$ENDEXTERNAL # End of external reference.");
                        break;
                    case SEBSDisassemblerQueueItemType.INTERRUPT:
                        generateBanner();
                        while ((baby = disassembleNext()) != BMSEvent.FINISH)
                            if (baby == BMSEvent.RETURN || baby==BMSEvent.RETI || baby == BMSEvent.REQUEST_STOP)
                                break;
                        break;
                    case SEBSDisassemblerQueueItemType.ENVELOPE:
                        generateBanner();
                        parseEnvelope();
                        break;
                    case SEBSDisassemblerQueueItemType.JUMPTABLE:
                        {
                            output.AppendLine();
                            output.AppendLine("ALIGN4");
                            generateBanner(true);
                            var addrs = readJumptable();
                            for (int i=0; i< addrs.Length; i++)
                            {
                                var address = addrs[i];
                                if (address==DummyAddress)
                                {
                                    output.AppendLine($"REF $DUMMY");
                                    continue;
                                }                                    
                                var label = getLabel("JTCALL", (int)address);
                                queueItems.Enqueue(new SEBSDissasemblerQueueItem()
                                {
                                    address = (int)address,
                                    label = label,
                                    type = SEBSDisassemblerQueueItemType.CALL
                                });
                                output.AppendLine($"REF {label}");
                            }
                            output.AppendLine("STOP #End Jumptable");
                            break;
                        }
                }
            }
        }
    }
}
