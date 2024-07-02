using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Syroot.BinaryData;

namespace GT4SoundTool.Formats.Ssqt.Meta;

public interface ISqMeta
{
    public void Read(BinaryStream bs);
}
