﻿//---------------------------------------------------------------------------------------
// SPDX-FileCopyrightText: Copyright (C) 2026 Nenkai
// SPDX-License-Identifier: MIT
// SPDX-FileAttributionText: https://github.com/Nenkai/GT4SoundTool
// SPDX-FileNotice: AI usage and consumption is prohibited. See AGENTS.md for policy.
//---------------------------------------------------------------------------------------

using Syroot.BinaryData;

namespace GT4SoundTool.Formats;

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
