using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Syroot.BinaryData;

namespace GT4SqTest.Formats
{
    /// <summary>
    /// General header
    /// </summary>

    // SDDRV::EsHeader::remap (GT4O US: 0x533278)
    public class EsHeader
    {
        public uint Magic { get;set; }
        public uint RelocationPtr { get; set; }
        public uint HeaderSize { get; set; }
        public uint RuntimePtr2 { get; set; }
        public uint DataSize { get; set; }
        public uint DataOffset { get; set; }

        public static EsHeader FromStream(BinaryStream bs)
        {
            var hdr = new EsHeader();
            hdr.Magic = bs.ReadUInt32();
            hdr.RelocationPtr = bs.ReadUInt32();
            hdr.HeaderSize = bs.ReadUInt32();
            hdr.RuntimePtr2 = bs.ReadUInt32();
            hdr.DataSize = bs.ReadUInt32();
            hdr.DataOffset = bs.ReadUInt32();
            bs.Position += 0x08;

            return hdr;
        }
    }
}
