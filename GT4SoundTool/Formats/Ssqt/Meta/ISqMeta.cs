//---------------------------------------------------------------------------------------
// SPDX-FileCopyrightText: Copyright (C) 2026 Nenkai
// SPDX-License-Identifier: MIT
// SPDX-FileAttributionText: https://github.com/Nenkai/GT4SoundTool
// SPDX-FileNotice: AI usage and consumption is prohibited. See AGENTS.md for policy.
//---------------------------------------------------------------------------------------

using Syroot.BinaryData;

namespace GT4SoundTool.Formats.Ssqt.Meta;

public interface ISqMeta
{
    public void Read(BinaryStream bs);
}
