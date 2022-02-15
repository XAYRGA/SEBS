using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SEBS
{
    internal struct SEBSOpenInstruction
    {
        public int OpenInstructionOffset;
        public byte TrackID;
    }
    internal struct SEBSJumptableData
    {
        public int InstructionInnerOffset;
        public int InterruptInstructionInnerOffset;
        public int SuggestedLength;
        
    }
    internal class SEBSCategory
    {
        public int CategoryIndex;
        public int RelocatableDataStart;
        public int RelocatableDataLength;
        public int DummyAddress;
        public SEBSOpenInstruction[] OpenInstructions = new SEBSOpenInstruction[1];
        public SEBSJumptableData JumpTable;    
    }
    internal class SEBSInfoFile
    {
        public string GameName; 
        public string GameVersion;
        public int InitalizationSectionBytes;
        public SEBSCategory[] Categories; 
    }
}
