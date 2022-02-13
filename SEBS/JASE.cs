using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Be.IO;
using System.IO;

namespace xayrga.asn
{
    /* 
ASN 
   ENDIAN big
   0x00 byte[0xE] = 0; 
   0x0D ushort total_wave_count 

   <categoryNames>
   ?? char[0x1C] NAME
   ?? ushort len 
   ?? ushort ID

   NOTE: Categories get sorted by their ID, and then read their wave counts in order.
   This means that if Section 10 has ID of 0030, and that section 1 has an ID of 0032 , that section 10's waves will be listed first. 
   The header has no indication of this, but this seems to be the layout. 

   <waves>
   ?? char[0x1C] NAME;
   ?? ushort access_mode (example, 8001 is the SEQUENCE_BGM arc in SMS, hardcoded maybe?) 
   ?? ushort ID


 ST 
   0x00 byte 0x06  
   0x01 byte ??
   0x02 byte ??
   0x03 byte ?? 
   0x04 ushort entryCount 
   0x0F categories[0x12]

   <Category>
   ushort count 
   ushort id 

   @HEADER_END 0x50;

   NOTE: Categories get sorted by their ID, and then read their wave counts in order.
   This means that if Section 10 has ID of 0030, and that section 1 has an ID of 0032 , that section 10's waves will be listed first. 
   The header has no indication of this, but this seems to be the layout. 

   <Waves>
   byte[16] unknown


   Additional notes: 

   The sound ID isn't based off of the actual ID attached to the sequence in the ASN. A sound ID is assigned based on the INDEX in the sound table (the order it's defined).
   Before compilation, all of the sound names are taken and transformed to their ID, then baked into the binary. 
*/


    public class JASNWave
    {
        public string name;
        public ushort mode;
        public ushort id;
        public int index;
    }

    public class JASNCategory
    {
        public string name;
        public ushort id;
        public ushort index;
        public JASNWave[] waves;
    }

    public class JASNTable
    {

        public JASNCategory[] categories = new JASNCategory[0x12]; // hardcoded 

        private static string readName(BeBinaryReader aafRead)
        {
            var ofs = aafRead.BaseStream.Position; // Store where we started 
            byte nextbyte; // Blank byte
            byte[] name = new byte[0x70]; // Array for the name

            int count = 0; // How many we've done
            while ((nextbyte = aafRead.ReadByte()) != 0xFF & nextbyte != 0x00) // Read until we've read 0 or FF
            {
                name[count] = nextbyte; // Store into byte array
                count++; // Count  how many valid bytes  we've read.
            }
            aafRead.BaseStream.Seek(ofs + 0x1C, SeekOrigin.Begin); // Seek 0x1C bytes, because thats the statically allocated space for the wavegroup path. 
            return Encoding.ASCII.GetString(name, 0, count); // Return a string with the name, but only of the valid bytes we've read. 
        }

        public static JASNTable readStream(BeBinaryReader br)
        {
            var Base = br.BaseStream.Position;
            var NewTable = new JASNTable();
            br.ReadBytes(0xE); // skip 0xD bytes
            var waveCount = br.ReadUInt16();
            for (int i = 0; i < 0x12; i++)
            {
                var cat = new JASNCategory();
                cat.name = readName(br);
                var count = br.ReadUInt16();
                cat.id = br.ReadUInt16();
                cat.waves = new JASNWave[count];
                cat.index = (ushort)i;
                NewTable.categories[i] = cat;

                //Console.ReadLine();
            }

            var catSorted = new JASNCategory[0x12];
            Array.Copy(NewTable.categories, catSorted, 0x12);
            for (int i = 0; i < 0x12; i++) // a third __fucking iteration__ on these stupid vectors.
            {
                for (int j = 0; j < 0x12; j++)
                {
                    var current = catSorted[i]; // Grab current oscillator vector, notice the for loop starts at 1
                    var cmp = catSorted[j]; // Grab the previous object
                    if (cmp.id > current.id) // if its time is greater than ours
                    {
                        catSorted[j] = current; // shift us down
                        catSorted[i] = cmp; // shift it up
                    }
                }
            }

            int totalOffset = 0;
            foreach (JASNCategory cat in catSorted)
                for (int i = 0; i < cat.waves.Length; i++)
                {
                    var newWave = new JASNWave()
                    {
                        name = readName(br),
                        mode = br.ReadUInt16(),
                        id = br.ReadUInt16(),
                        index = totalOffset,
                    };
                    cat.waves[i] = newWave;
                    totalOffset++;
                }

            return NewTable;
        }
    }
}
