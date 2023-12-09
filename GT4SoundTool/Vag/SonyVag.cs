using System;
using System.IO;
using System.Text;

namespace GT4SqTest.Vag
{
    public static partial class SonyVag
    {
        private struct VAGChunk
        {
            public sbyte shift;
            public sbyte predict; /* swy: reversed nibbles due to little-endian */
            public byte flags;
            public byte[] sample;
        };

        private enum VAGFlag
        {
            VAGF_NOTHING = 0,         /* Nothing*/
            VAGF_LOOP_LAST_BLOCK = 1, /* Last block to loop */
            VAGF_LOOP_REGION = 2,     /* Loop region*/
            VAGF_LOOP_END = 3,        /* Ending block of the loop */
            VAGF_LOOP_FIRST_BLOCK = 4,/* First block of looped data */
            VAGF_UNK = 5,             /* ?*/
            VAGF_LOOP_START = 6,      /* Starting block of the loop*/
            VAGF_PLAYBACK_END = 7     /* Playback ending position */
        };

        private static readonly int VAG_SAMPLE_BYTES = 14;
        private static readonly int VAG_SAMPLE_NIBBL = VAG_SAMPLE_BYTES * 2;

        public static uint GetLoopOffsetForVag(uint loopOffset)
        {
            uint loopOffsetVag = (uint)(loopOffset / 28 + (((loopOffset % 28) != 0) ? 2 : 1));
            return loopOffsetVag;
        }
    }
}
