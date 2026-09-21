using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using GT4SoundTool.Vag;

using Syroot.BinaryData;

namespace GT4SoundTool.Formats.Jam;

// Game code refers to this as "JamSplitChunk"
// JAM original documents refer to this as "SplitBlock"
public class JamProgChunk
{
    /// <summary>
    /// If 0xFF = has ALL notes provided for a certain range using <see cref="StartNoteRange" /> and <see cref="EndNoteRange" /> <br />
    /// Otherwise lower 4 bits + 1 <br />
    /// Notes in ranges can still be empty
    /// </summary>
    public byte CountOrFlag { get; set; }

    /// <summary>
    /// Volume of the whole program. Will get multiplied by <see cref="JamSplitChunk.Volume"/>
    /// </summary>
    public byte BaseVolume { get; set; }

    /// <summary>
    /// <b>Maybe</b> pan for the program, but possibly unused
    /// </summary>
    public byte Pan { get; set; }

    /// <summary>
    /// Unknown, Possibly unused
    /// </summary>
    public byte field_0x03 { get; set; }

    /// <summary>
    /// Only used if <see cref="JamSplitChunk.Flags"/> has 0x10
    /// </summary>
    public byte PitchBendSensitivity { get; set; }

    /// <summary>
    /// Lfo table index. 0x7F = no lfo in use <br/>
    /// Only used if <see cref="JamSplitChunk.Flags"/> has 0x40
    /// </summary>
    public byte LfoTableIndex { get; set; }

    /// <summary>
    /// Starting note range
    /// </summary>
    public Note StartNoteRange { get; set; }

    /// <summary>
    /// End note range
    /// </summary>
    public Note EndNoteRange { get; set; }

    public List<JamSplitChunk> SplitChunks { get; set; } = new List<JamSplitChunk>();

    public void Read(BinaryStream bs)
    {
        CountOrFlag = bs.Read1Byte();
        BaseVolume = bs.Read1Byte();
        Pan = bs.Read1Byte();
        field_0x03 = bs.Read1Byte();
        PitchBendSensitivity = bs.Read1Byte();
        LfoTableIndex = bs.Read1Byte();
        StartNoteRange = (Note)bs.Read1Byte();
        EndNoteRange = (Note)bs.Read1Byte();

        if (CountOrFlag == 0xFF)
        {
            int cnt = (EndNoteRange - StartNoteRange) + 1;
            for (int i = 0; i < cnt; i++)
            {
                var splitChunk = new JamSplitChunk();
                splitChunk.Read(bs);
                SplitChunks.Add(splitChunk);
            }
        }
        else
        {
            int cnt = (CountOrFlag & 0x0F) + 1;
            for (int i = 0; i < cnt; i++)
            {
                var splitChunk = new JamSplitChunk();
                splitChunk.Read(bs);
                SplitChunks.Add(splitChunk);
            }
        }
    }
}

public class JamSplitChunk
{
    public Note NoteMin { get; set; }
    public Note NoteMax { get; set; }
    public Note BaseNote { get; set; }

    /// <summary>
    /// Pitch. 1 = 1/16 semitones
    /// </summary>
    public sbyte Pitch { get; set; }


    public JamSplitChunkFlags Flags { get; set; }

    /// <summary>
    /// <b>Offset of vag data (ssa, sample start address).</b><br/>
    /// Multiply by 0x10 for sample data offset starting from bd offset.<br />
    /// <br/>
    /// PlayStation 2 IOP Library Reference Release 3.0.2 - Sound Libraries<br />
    /// "Low-Level Sound Library - Register Macros" Page 66, SD_VA_SSA.<br />
    /// <br />
    /// This is sent through PDISPU2 with:
    /// <code>sceSdSetAddr(coreAndVoice | 0x2040, ADJ(voice)->SD_VA_SSA)</code>
    /// </summary>
    public uint SD_VA_SSA { get; set; }

    /// <summary>
    /// <b>Envelope (data 1)</b><br/>
    /// <br/>
    /// PlayStation 2 IOP Library Reference Release 3.0.2 - Sound Libraries<br />
    /// "Low-Level Sound Library - Register Macros" Page 67, SD_VP_ADSR1.<br />
    /// <br />
    /// This is sent through PDISPU2 with:
    /// <code>sceSdSetParam(coreAndVoice | 0x300, (unsigned __int16)ADJ(voice)->SD_VP_ADSR1)</code>
    /// </summary>
    public short SD_VP_ADSR1 { get; set; }

    /// <summary>
    /// <b>Envelope (data 2)</b><br/>
    /// <br/>
    /// PlayStation 2 IOP Library Reference Release 3.0.2 - Sound Libraries<br />
    /// "Low-Level Sound Library - Register Macros" Page 68, SD_VP_ADSR2.<br />
    /// <br />
    /// This is sent through PDISPU2 with:
    /// <code>sceSdSetParam(coreAndVoice | 0x400, ADJ(voice)->SD_VP_ADSR2);</code>
    /// </summary>
    public short SD_VP_ADSR2 { get; set; }

    /// <summary>
    /// 0-127, where 127 = full volume
    /// </summary>
    public byte Volume { get; set; }

    /// <summary>
    /// 64 is default (center).
    /// </summary>
    public byte Pan { get; set; }

    /// <summary>
    /// Number of semitones the pitch shifts at full pitch-wheel deflection? Only used if <see cref="Flags"/> has 0x10, otherwise <see cref="JamProgChunk.PitchBendSensitivity"/> is used.
    /// </summary>
    public byte PitchBendSensitivity { get; set; }

    /// <summary>
    /// Lfo table index. Only used if <see cref="Flags"/> does NOT have 0x40, otherwise <see cref="JamProgChunk.LfoTableIndex"/> is used. <br />
    /// 0x7F = no lfo in use
    /// </summary>
    public byte LfoTableIndex { get; set; }

    public void Read(BinaryStream bs)
    {
        NoteMin = (Note)bs.Read1Byte();
        NoteMax = (Note)bs.Read1Byte();
        BaseNote = (Note)bs.Read1Byte();
        Pitch = bs.ReadSByte();
        Flags = (JamSplitChunkFlags)bs.Read1Byte();
        SD_VA_SSA = (uint)((bs.Read1Byte() << 16) | bs.ReadUInt16()); // Game code refers to the offset to audio as Ssa
        SD_VP_ADSR1 = bs.ReadInt16();
        SD_VP_ADSR2 = bs.ReadInt16();
        Volume = bs.Read1Byte();
        Pan = bs.Read1Byte();
        PitchBendSensitivity = bs.Read1Byte();
        LfoTableIndex = bs.Read1Byte();
    }

    public byte[] GetData(BinaryStream bs, out uint loopStart, out uint loopEnd)
    {
        loopStart = 0;
        loopEnd = 0;

        // Size of vag is not provided, we must find it using vag flags
        long basePos = bs.Position;
        long absoluteSsaOffset = basePos + (SD_VA_SSA * 0x10);
        bs.Position = absoluteSsaOffset;

        uint lastSampleIndex = 0;
        while (bs.Position < bs.Length)
        {
            byte decodingCoef = bs.Read1Byte();
            SonyVag.VAGFlag flag = (SonyVag.VAGFlag)bs.Read1Byte();

            if (flag == SonyVag.VAGFlag.VAGF_LOOP_START)
                loopStart = lastSampleIndex;

            if (flag == SonyVag.VAGFlag.VAGF_LOOP_LAST_BLOCK || flag == SonyVag.VAGFlag.VAGF_LOOP_END)
            {
                if (flag == SonyVag.VAGFlag.VAGF_LOOP_END)
                    loopEnd = lastSampleIndex;

                break;
            }
                

            lastSampleIndex++;

            bs.Position += 0x0E;
        }

        bs.Position = absoluteSsaOffset;
        return bs.ReadBytes(0x10 * (int)(lastSampleIndex + 1));
    }

    public override string ToString()
    {
        return $"{NoteMin}->{NoteMax}";
    }

    [Flags]
    public enum JamSplitChunkFlags : byte
    {
        UnkBit1 = 1 << 0, // 0x01

        /// <summary>
        /// SE only
        /// </summary>
        SetNoiseShiftFrequency = 1 << 1, // 0x02

        /// <summary>
        /// Use prog chunk's PitchBendSensitivity instead of split chunk's
        /// </summary>
        UsePitchBendSensitivityFromProgChunk = 1 << 4, // 0x10

        PitchModulateSpeedAndDepth = 1 << 5, // 0x20

        /// <summary>
        /// Use prog chunk's LfoTableIndex instead of split chunk's
        /// </summary>
        UseLfoTableIndexFromProgChunk = 1 << 6, // 0x40

        /// <summary>
        /// Whether to use mixing for effects (reverb, sets SD_S_VMIXL & SD_S_VMIXR)
        /// </summary>
        MixingReverb = 1 << 7, // 0x80
    }
}
