using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using xayrga.asn;
using Be.IO;

namespace SEBS
{
    public enum SEBAssemblerLabelType
    {
        INT8 = 0,
        INT16 = 1,
        INT24 = 2, 
        INT32 = 3 
    }

    class SEBSAssemblerLabelRef
    {
        public long address;
        public string label;
        public SEBAssemblerLabelType size;
    }

    class SEBSAssemblerCommand
    {
        public string command;
        public string[] args;
        public int line;
    }

    class SEBSBMSAssembler
    {
        string stackName = "";

        long baseAddress = 0;

        public int DummyAddress = 0; 

        BeBinaryWriter output;

        Dictionary<string, long> labelStorage = new Dictionary<string, long>();
        Queue<SEBSAssemblerLabelRef> labelReferences = new Queue<SEBSAssemblerLabelRef>();
      
        SEBSAssemblerCommand[] commands;

        SEBSAssemblerCommand currentCommand;

        string[] lines;
        public SEBSBMSAssembler(string stackN, BeBinaryWriter op, string[] textData) {
            lines = textData;
            stackName = stackN;
            output = op;        
        }

        private int parseNumber(string num)
        {
            var ns = System.Globalization.NumberStyles.Integer;
            if (num.Length >= 2 && num[0] == '0' && num[1] == 'x')
            {
                ns = System.Globalization.NumberStyles.HexNumber;
                num = num.Substring(2);
            }
            else if (num.Length >= 1 && num[0] == 'h')
            {
                ns = System.Globalization.NumberStyles.HexNumber;
                num = num.Substring(1);
            }
            var oV = -1;
            if (!Int32.TryParse(num, ns, null, out oV))
                compileError($"Failed to parse number: '{num}'");
            return oV;
        }

        private void compileError(string reason)
        {
            throw new Exception($"{reason} [{stackName}] @ Line {currentCommand.line} command {currentCommand.command}");
        }
        private void storeLabelRef(string name, SEBAssemblerLabelType type)
        {
            if (name[0] == '$') // Target. 
            {
                var address = name.Substring(1);
                var bb = Int32.Parse(address, System.Globalization.NumberStyles.HexNumber);
                switch (type)
                {
                    case SEBAssemblerLabelType.INT8:
                        output.Write((byte)bb);
                        break;
                    case SEBAssemblerLabelType.INT16:
                        output.Write((short)bb);
                        break;
                    case SEBAssemblerLabelType.INT24:
                        output.WriteU24(bb);
                        break;
                    case SEBAssemblerLabelType.INT32:
                        output.WriteU24(bb);
                        break;
                    default:
                        throw new Exception("Whoops.");
                        break;
                       
                }
                return;
            }

            labelReferences.Enqueue(new SEBSAssemblerLabelRef()
            {
                address = output.BaseStream.Position,
                label = name,
                size = type
            });

            switch (type)
            {
                case SEBAssemblerLabelType.INT8:
                    output.Write(new byte[1]);
                    break;
                case SEBAssemblerLabelType.INT16:
                    output.Write(new byte[2]);
                    break;
                case SEBAssemblerLabelType.INT24:
                    output.Write(new byte[3]);
                    break;
                case SEBAssemblerLabelType.INT32:
                    output.Write(new byte[4]);
                    break;
                default:
                    throw new Exception("Whoops.");
                    break;
            }
        }

        public void writeBMSEvent(BMSEvent bb)
        {
            output.Write((byte)bb);
        }
               
        public void writeTEN(string ten)
        {
            var type = ten[0];
            var num = ten.Substring(1);
            switch (type)
            {
                case 'b':
                    {
                        byte bv = 0;
                        if (!byte.TryParse(num, System.Globalization.NumberStyles.HexNumber, null,out bv))
                            compileError($"Failed to parse Type-Encoded-Number {type} (Is it hexadecimal?)");
                        output.Write(num);
                        break;
                    }
                case 'w':
                    {
                        int bv = 0;
                        if (int.TryParse(num, System.Globalization.NumberStyles.HexNumber, null, out bv))
                            compileError($"Failed to parse Type-Encoded-Number {type} (Is it hexadecimal?)");
                        output.Write(num);
                        break;
                    }
                case 'h':
                    {
                        short bv = 0;
                        if (short.TryParse(num, System.Globalization.NumberStyles.HexNumber, null, out bv))
                            compileError($"Failed to parse Type-Encoded-Number {type} (Is it hexadecimal?)");
                        output.Write(num);
                        break;
                    }
                case 'c':
                    {
                        byte bv = 0;
                        if (!byte.TryParse(num, out bv))
                            compileError($"Failed to parse Type-Encoded-Number {type}");
                        output.Write(num);
                        break;
                    }
                default:
                    compileError($"Unknown Type Encoded Number specifier {type}");
                    break;
            }
        }


        public void fillCommands()
        {
            var cnt = 0;
            Queue<SEBSAssemblerCommand> commandBuilder = new Queue<SEBSAssemblerCommand>();
            for (int line=0; line < lines.Length; line++)
            {
                var curLine = lines[line];
                if (curLine == null || curLine.Length < 2 || curLine.Length == 0) // Skip empty lines
                    continue;
                if (curLine[0] == '#') // Ignore comments. 
                    continue;
                var lineExp = curLine.Split(' ');
                if (lineExp.Length <= 0) // Something stupid?
                    continue;
                var command = lineExp[0];
                var args = new string[lineExp.Length - 1];
                for (int i = 1; i < lineExp.Length; i++)
                    args[i - 1] = lineExp[i];
                var preObj = new SEBSAssemblerCommand()
                {
                    line = line,
                    args = args,
                    command = command
                };
                Console.WriteLine(preObj);
                Console.WriteLine($"{cnt} preload {preObj} {preObj.command}");
                commandBuilder.Enqueue(preObj);
                cnt++;
            }

            commands = new SEBSAssemblerCommand[commandBuilder.Count];
            for (int i = 0; i < commands.Length; i++)
                commands[i] = commandBuilder.Dequeue();
        }

        int commandID = 0;

        public void assembleAll()
        {
         
            for (int i=0; i < commands.Length; i++)
            {
                var command = commands[i];
                assemble(command);
            }
            dereferenceLabels();
        }

        public void dereferenceLabels()
        {
            var labelReferenceCount = labelReferences.Count;
            for (int i=0; i < labelReferenceCount; i++)
            {
                var currentRef = labelReferences.Dequeue();
                if (!labelStorage.ContainsKey(currentRef.label))
                    compileError($"Failed to dereference label {currentRef.label}");
                var storedLabelPos = labelStorage[currentRef.label];
                output.BaseStream.Position = currentRef.address;
                if (currentRef.size == SEBAssemblerLabelType.INT24)
                    output.WriteU24((int)storedLabelPos);
            }
        }

        bool waitingForAgs = false;
        public void assemble(SEBSAssemblerCommand DisCommand)
        {

            currentCommand = DisCommand;
            var comText = DisCommand.command;
            var args = DisCommand.args;

            if (comText[0] == ':')
                labelStorage[comText.Substring(1)] = output.BaseStream.Position;
            else {

                if (waitingForAgs & comText != "$WRITEARGS")
                    compileError("Expecting writeargs after previous command! BMS Data will be misaligned.");

                switch (comText)
                {

                    case "NOTEON1":
                        output.Write((byte)parseNumber(args[0]));
                        output.Write((byte)parseNumber(args[1]));
                        output.Write((byte)parseNumber(args[2]));
                        break;
                    case "NOTEON2":
                        output.Write((byte)parseNumber(args[0]));
                        output.Write((byte)parseNumber(args[1]));
                        output.Write((byte)parseNumber(args[2]));
                        output.Write((byte)parseNumber(args[3]));
                        output.Write((byte)parseNumber(args[4]));
                        break;
                    case "NOTEON3":
                        output.Write((byte)parseNumber(args[0]));
                        output.Write((byte)parseNumber(args[1]));
                        output.Write((byte)parseNumber(args[2]));
                        output.Write((byte)parseNumber(args[3]));
                        output.Write((byte)parseNumber(args[4]));
                        output.Write((byte)parseNumber(args[5]));
                        break;
                    case "NOTEON4":
                        output.Write((byte)parseNumber(args[0]));
                        output.Write((byte)parseNumber(args[1]));
                        output.Write((byte)parseNumber(args[2]));
                        output.Write((byte)parseNumber(args[3]));
                        output.Write((byte)parseNumber(args[4]));
                        output.Write((byte)parseNumber(args[5]));
                        output.Write((byte)parseNumber(args[6]));
                        break;

                    case "WAIT8":
                        writeBMSEvent(BMSEvent.CMD_WAIT8);
                        output.Write((byte)parseNumber(args[0]));
                        break;
                    case "WAIT16":
                        writeBMSEvent(BMSEvent.CMD_WAIT8);
                        output.Write((ushort)parseNumber(args[0]));
                        break;
                    case "SET_BANK_INST":
                        writeBMSEvent(BMSEvent.PARAM_SET_16);
                        output.Write((byte)0x06); // Instrument
                        output.Write((byte)parseNumber(args[0]));
                        output.Write((byte)parseNumber(args[1]));
                        break;
                    case "PARAM_SET_16":
                        writeBMSEvent(BMSEvent.PARAM_SET_16);
                        output.Write((byte)parseNumber(args[0]));
                        output.Write((short)parseNumber(args[1]));
                        break;
                    case "OPENTRACK":
                        writeBMSEvent(BMSEvent.OPENTRACK);
                        output.Write((byte)parseNumber(args[0]));
                        storeLabelRef(args[1], SEBAssemblerLabelType.INT24);
                        break;
                    case "JMP":
                        writeBMSEvent(BMSEvent.JMP);
                        output.Write((byte)parseNumber(args[0]));
                        storeLabelRef(args[1], SEBAssemblerLabelType.INT24);
                        break;
                    case "CALL":
                        writeBMSEvent(BMSEvent.CALL);
                        output.Write((byte)parseNumber(args[0]));
                        storeLabelRef(args[1], SEBAssemblerLabelType.INT24);
                        break;
                    case "CALLTABLE":
                        writeBMSEvent(BMSEvent.CALL);
                        output.Write((byte)0xc0);
                        output.Write((byte)parseNumber(args[0]));
                        storeLabelRef(args[1], SEBAssemblerLabelType.INT24);
                        break;
                    case "SIMPLEENV":
                        writeBMSEvent(BMSEvent.SIMPLEENV);
                        output.Write((byte)parseNumber(args[0]));
                        storeLabelRef(args[1], SEBAssemblerLabelType.INT24);
                        break;
                    case "INTERRUPT":
                        writeBMSEvent(BMSEvent.SIMPLEENV);
                        output.Write((byte)parseNumber(args[0]));
                        storeLabelRef(args[1], SEBAssemblerLabelType.INT24);
                        break;
                    case "PRINTF":
                        {
                            writeBMSEvent(BMSEvent.PRINTF);
                            for (int i = 0; i < args.Length; i++)
                                output.Write(Encoding.ASCII.GetBytes(args[i]));
                            output.Write((byte)0x00); // null terminator
                            // If $WRITEARGS doesn't follow after this. You're fucked :) 
                            waitingForAgs = true; 
                        }
                        break;
                    case "$WRITEARGS":
                        {
                            for (int i = 0; i < args.Length; i++)
                                writeTEN(args[i]);
                            waitingForAgs = false;
                        }
                        break;
                    case "REF":
                        if (args[1] == "$DUMMY")
                            output.WriteU24(DummyAddress);
                        else
                            storeLabelRef(args[1], SEBAssemblerLabelType.INT24);
                        break;
                    case "ENV":
                        output.Write((short)parseNumber(args[1]));
                        break;
                    case "STOP": // used for parsing commands
                        break;
                    case "CLOSETRACK":
                        writeBMSEvent(BMSEvent.CLOSETRACK);
                        output.Write((byte)parseNumber(args[0]));
                        break;
                    case "SIMPLEOSC":
                        writeBMSEvent(BMSEvent.SIMPLEOSC);
                        output.Write((byte)parseNumber(args[0]));
                        break;
                    case "TRANSPOSE":
                        writeBMSEvent(BMSEvent.TRANSPOSE);
                        output.Write((byte)parseNumber(args[0]));
                        break;
                    case "CLEARINTERRUPT":
                        writeBMSEvent(BMSEvent.CLRI);
                        break;
                    case "RETURNINTERRUPT":
                        writeBMSEvent(BMSEvent.CLRI);
                        break;
                    case "OSCROUTE":
                        writeBMSEvent(BMSEvent.OSCROUTE);
                        output.Write((byte)parseNumber(args[0]));
                        break;
                    case "VIBDEPTH":
                        writeBMSEvent(BMSEvent.VIBDEPTH);
                        output.Write((byte)parseNumber(args[0]));
                        break;
                    case "VIBDEPTHMIDI":
                        writeBMSEvent(BMSEvent.VIBDEPTHMIDI);
                        output.Write((byte)parseNumber(args[0]));
                        output.Write((byte)parseNumber(args[1]));
                        break;
                    case "VIBPITCH":
                        writeBMSEvent(BMSEvent.VIBPITCH);
                        output.Write((byte)parseNumber(args[0]));
                        break;
                    case "FLUSHALL":
                        writeBMSEvent(BMSEvent.FLUSHALL);
                        break;
                    case "IIRCUTOFF":
                        writeBMSEvent(BMSEvent.IIRCUTOFF);
                        output.Write((byte)parseNumber(args[0]));
                        break;
                    case "SIMPLEADSR":
                        writeBMSEvent(BMSEvent.SIMPLEADSR);
                        output.Write((short)parseNumber(args[0]));
                        output.Write((short)parseNumber(args[1]));
                        output.Write((short)parseNumber(args[2]));
                        output.Write((short)parseNumber(args[3]));
                        output.Write((short)parseNumber(args[4]));
                        break;
                    case "READPORT":
                        writeBMSEvent(BMSEvent.READPORT);
                        output.Write((byte)parseNumber(args[0]));
                        output.Write((byte)parseNumber(args[1]));
                        break;
                    case "WRITEPORT":
                        writeBMSEvent(BMSEvent.WRITEPORT);
                        output.Write((byte)parseNumber(args[0]));
                        output.Write((byte)parseNumber(args[1]));
                        break;
                    case "CHILDWRITEPORT":
                        writeBMSEvent(BMSEvent.CHILDWRITEPORT);
                        output.Write((byte)parseNumber(args[0]));
                        output.Write((byte)parseNumber(args[1]));
                        break;
                    case "PERF_S8_DUR_U16":
                        writeBMSEvent(BMSEvent.PERF_S8_DUR_U16);
                        output.Write((byte)parseNumber(args[0]));
                        output.Write((sbyte)parseNumber(args[1]));
                        output.Write((ushort)parseNumber(args[2]));
                        break;
                    case "PERF_S16":
                        writeBMSEvent(BMSEvent.PERF_S16_NODUR);
                        output.Write((byte)parseNumber(args[0]));
                        output.Write((short)parseNumber(args[1]));
                        break;
                    case "PERF_S16_DUR_U8_9E":
                        writeBMSEvent(BMSEvent.PERF_S16_DUR_U8_9E);
                        output.Write((byte)parseNumber(args[0]));
                        output.Write((short)parseNumber(args[1]));
                        output.Write((byte)parseNumber(args[2]));
                        break;
                    case "PERF_S16_U8":
                        writeBMSEvent(BMSEvent.PERF_S16_DUR_U8);
                        output.Write((byte)parseNumber(args[0]));
                        output.Write((short)parseNumber(args[1]));
                        output.Write((byte)parseNumber(args[2]));
                        break;
                    case "PERF_S8":
                        writeBMSEvent(BMSEvent.PERF_S8_NODUR);
                        output.Write((byte)parseNumber(args[0]));
                        output.Write((sbyte)parseNumber(args[1]));
                        break;
                    case "PAR_SET_REG":
                        writeBMSEvent(BMSEvent.PARAM_SET_R);
                        output.Write((byte)parseNumber(args[0]));
                        output.Write((byte)parseNumber(args[1]));
                        break;
                    case "PAR_ADD_REG":
                        writeBMSEvent(BMSEvent.PARAM_ADD_R);
                        output.Write((byte)parseNumber(args[0]));
                        output.Write((byte)parseNumber(args[1]));
                        break;
                    case "PARAM_BITWISE_C":
                        writeBMSEvent(BMSEvent.PARAM_BITWISE);
                        output.Write((byte)parseNumber(args[0]));
                        output.Write((byte)parseNumber(args[1]));
                        output.Write((byte)parseNumber(args[2]));
                        output.Write((byte)parseNumber(args[3]));
                        break;
                    case "PARAM_BITWISE_8":
                        writeBMSEvent(BMSEvent.PARAM_BITWISE);
                        output.Write((byte)parseNumber(args[0]));
                        output.Write((byte)parseNumber(args[1]));
                        output.Write((byte)parseNumber(args[2]));
                        break;
                    case "PARAM_BITWISE":
                        writeBMSEvent(BMSEvent.PARAM_BITWISE);
                        output.Write((byte)parseNumber(args[0]));
                        output.Write((byte)parseNumber(args[1]));
                        output.Write((byte)parseNumber(args[2]));
                        break;
                    case "PERF_S8_U8":
                        writeBMSEvent(BMSEvent.PERF_S8_DUR_U8);
                        output.Write((byte)parseNumber(args[0]));
                        output.Write((sbyte)parseNumber(args[1]));
                        output.Write((byte)parseNumber(args[2]));
                        break;
                    case "PAR_SET_8":
                        writeBMSEvent(BMSEvent.PARAM_SET_8);
                        output.Write((byte)parseNumber(args[0]));
                        output.Write((byte)parseNumber(args[1]));
                        break;
                    case "PAR_ADD_8":
                        writeBMSEvent(BMSEvent.PARAM_ADD_8);
                        output.Write((byte)parseNumber(args[0]));
                        output.Write((byte)parseNumber(args[1]));
                        break;
                    case "PAR_ADD_16":
                        writeBMSEvent(BMSEvent.PARAM_ADD_16);
                        output.Write((byte)parseNumber(args[0]));
                        output.Write((short)parseNumber(args[1]));
                        break;
                    case "PAR_CMP_8":
                        writeBMSEvent(BMSEvent.PARAM_CMP_8);
                        output.Write((byte)parseNumber(args[0]));
                        output.Write((byte)parseNumber(args[1]));
                        break;
                    case "PAR_CMP_R":
                        writeBMSEvent(BMSEvent.PARAM_CMP_R);
                        output.Write((byte)parseNumber(args[0]));
                        output.Write((byte)parseNumber(args[1]));
                        break;
                    case "PAR_SET_90":
                        writeBMSEvent(BMSEvent.SETPARAM_90);
                        output.Write((byte)parseNumber(args[0]));
                        output.Write((byte)parseNumber(args[1]));
                        break;
                    case "SETLN":
                        writeBMSEvent(BMSEvent.SETLASTNOTE);
                        output.Write((byte)parseNumber(args[0]));
                        break;
                    case "WAITR":
                        writeBMSEvent(BMSEvent.CMD_WAITR);
                        output.Write((byte)parseNumber(args[0]));
                        break;
                    case "LOOPS":
                        writeBMSEvent(BMSEvent.LOOP_S);
                        output.Write((byte)parseNumber(args[0]));
                        break;
                    case "LOOPE":
                        writeBMSEvent(BMSEvent.LOOP_E);
                        break;
                    case "SNCCPU":
                        writeBMSEvent(BMSEvent.SYNCCPU);
                        output.Write((short)parseNumber(args[0]));
                        break;
                    case "TEMPO":
                        writeBMSEvent(BMSEvent.TEMPO);
                        output.Write((short)parseNumber(args[0]));
                        break;
                    case "TBASE":
                        writeBMSEvent(BMSEvent.TIMEBASE);
                        output.Write((short)parseNumber(args[0]));
                        break;
                    case "RETURN":
                        writeBMSEvent(BMSEvent.RETURN);
                        output.Write((byte)parseNumber(args[0]));
                        break;
                    case "RETURN_NOARG":
                        writeBMSEvent(BMSEvent.RETURN_NOARG);
                        break;
                    case "FINISH":
                        writeBMSEvent(BMSEvent.FINISH);
                        break;
                    default:
                        compileError($"Unrecognized instruction {DisCommand}");
                        break;

                }
            }

        }
    }
}
