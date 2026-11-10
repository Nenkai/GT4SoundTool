//---------------------------------------------------------------------------------------
// SPDX-FileCopyrightText: Copyright (C) 2026 Nenkai
// SPDX-License-Identifier: MIT
// SPDX-FileAttributionText: https://github.com/Nenkai/GT4SoundTool
// SPDX-FileNotice: AI usage and consumption is prohibited. See AGENTS.md for policy.
//---------------------------------------------------------------------------------------

using Syroot.BinaryData;

namespace GT4SoundTool.Formats.Ssqt.Meta;

public class SqSetTempoEvent : ISqMeta
{
    public uint UsecPerQuarterNote { get; set; }

    public void Read(BinaryStream bs)
    {
        UsecPerQuarterNote = (uint)(bs.ReadByte() << 16 | bs.Read1Byte() << 8 | bs.Read1Byte());
    }
}
