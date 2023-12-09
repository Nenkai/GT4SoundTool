using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Syroot.BinaryData;

namespace GT4SqTest.Formats.Se.Meta
{
    public interface ISeMeta
    {
        public void Read(BinaryStream bs);
    }
}
