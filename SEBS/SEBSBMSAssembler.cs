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
                ns = System.Globalization.NumberStyles.HexNumber;
            var oV = -1;
            if (!Int32.TryParse(num, out oV))
                throw new Exception($"Failed to parse number: '{num}' [{stackName} @ Line {currentCommand.line}");
            return oV;
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



        public void fillCommands()
        {
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
                    args[i] = lineExp[i];
                commandBuilder.Enqueue(new SEBSAssemblerCommand()
                {
                    line = line,
                    args = args,
                    command = command
                });
            }

            commands = new SEBSAssemblerCommand[commandBuilder.Count];
            for (int i = 0; i < commandBuilder.Count; i++)
                commands[i] = commandBuilder.Dequeue();
        }

        int commandID = 0; 
        public void assembleNext()
        {
            var command = commands[commandID];
            currentCommand = command;
            var comText = command.command;

            if (comText[0] == ':')
                labelStorage[comText.Substring(1)] = output.BaseStream.Position;
            else 
                switch (comText)
                {

                }

        }



    }
}
