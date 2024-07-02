using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Syroot.BinaryData;

using GT4SoundTool.Formats.Ssqt;
using GT4SoundTool.Formats.Ssqt.Meta;

namespace GT4SoundTool.Formats.Ssqt;

// Sqt is handled by game code through one of the two sequencers - SqSequencer, for menu bgm.
// Sq is somewhat named in the JAM sdk docs.

// Spu sequence Table? Sound sequence table?
public class Ssqt
{
    public List<Sssq> Tracks { get; set; } = new List<Sssq>();

    public void Read(string fileName)
    {
        using var fs = new FileStream(fileName, FileMode.Open);
        using var bs = new BinaryStream(fs, ByteConverter.Little);

        uint magic = bs.ReadUInt32();
        if (magic != 0x74717353)
            throw new InvalidDataException();

        uint numSequences = bs.ReadUInt32();
        uint[] sequenceOffsets = bs.ReadUInt32s((int)numSequences + 1);

        for (int i = 0; i < numSequences + 1; i++)
        {
            bs.Position = sequenceOffsets[i];
            var sssq = new Sssq();
            sssq.Read(bs);

            Tracks.Add(sssq);
        }
    }

    // BIG NOTE: 7bit int implementation is different!
    public static long readVariable(BinaryStream bs)
    {
        long result = 0;
        byte @byte;

        do
        {
            @byte = bs.Read1Byte();
            result = (result << 7) + (@byte & 0x7F);
        }
        while ((@byte & 0x80) != 0);
        return result;
    }
}

// Spu stream sequence?
public class Sssq
{
    public uint TicksPerBeat { get; set; }
    public List<SqMessage> Messages { get; set; } = new();

    public void Read(BinaryStream bs)
    {
        uint magic = bs.ReadUInt32();
        if (magic != 0x71737353)
            throw new InvalidDataException();
        TicksPerBeat = bs.ReadUInt32();

        // Starting from here, closely matches midi format specification
        byte lastStatus = 0;
        while (true)
        {
            var message = new SqMessage();
            message.Read(bs, lastStatus);
            if (message.Status == 0xFF)
            {
                var meta = message.Event as SqMetaEvent;
                if (meta.Type == 0x2F)
                    break;
            }

            Messages.Add(message);
            lastStatus = message.Status;
        }
    }
}

public class SqMessage
{
    public byte Status { get; set; }
    public uint Delta { get; set; }
    public ISqEvent Event { get; set; }

    public void Read(BinaryStream bs, byte lastStatus)
    {
        // SDDRV::SqSequencer::statusEventCaller (GT4O US: 0x535238) 
        // (yes i'm reading the delta first here)
        Delta = (uint)Ssqt.readVariable(bs);
        byte status = bs.Read1Byte();
        if ((status & 0x80) != 0)
            Status = status;
        else
        {
            Status = lastStatus;
            bs.Position -= 1;
        }

        // This is all that's supported by the SqSequencer
        // Note that SeSequencer may support more events/meta (not implemented for now, it's used by sfx - midi is bundled inside the ins header in that case)
        ISqEvent @event;
        if ((Status & 0xF0) == 0x80) // SDDRV::SqSequencer::ev_8x (GT4O US: 0x535350)
        {
            Event = new SqNoteOffEvent();
            Event.Read(bs);
        }
        else if ((Status & 0xF0) == 0x90) // SDDRV::SqSequencer::ev_9x (GT4O US: 0x535390)
        {
            Event = new SqNoteOnEvent();
            Event.Read(bs);
        }
        else if ((Status & 0xF0) == 0xB0) // SDDRV::SqSequencer::ev_Bx (GT4O US: 0x535400)
        {
            Event = new SqControllerEvent();
            Event.Read(bs);
        }
        else if ((Status & 0xF0) == 0xC0) // SDDRV::SqSequencer::ev_Cx (GT4O US: 0x535700)
        {
            Event = new SqProgramEvent();
            Event.Read(bs);
        }
        else if ((Status & 0xF0) == 0xE0) // SDDRV::SqSequencer::ev_Ex (GT4O US: 0x535770)
        {
            Event = new SqPitchBendEvent();
            Event.Read(bs);
        }
        else if (status == 0xFF) // SDDRV::SqSequencer::ev_Fx (GT4O US: 0x535820)
        {
            Event = new SqMetaEvent();
            Event.Read(bs);

            if (Event is SqMetaEvent meta && meta.Type == 0x2F)
                return;
        }
    }
}

public class SqNoteOnEvent : ISqEvent
{
    public Note Note { get; set; }

    /// <summary>
    /// NOTE: This is used and indexed to the Jam velocity table first to then get the real velocity
    /// Think of this as a velocity entry index
    /// </summary>
    public byte Velocity { get; set; }

    public void Read(BinaryStream bs)
    {
        Note = (Note)bs.Read1Byte();
        Velocity = bs.Read1Byte();
    }
}

public class SqNoteOffEvent : ISqEvent
{
    public Note Note { get; set; }

    public void Read(BinaryStream bs)
    {
        Note = (Note)bs.Read1Byte();
        // No velocity in Sq
    }
}


public class SqControllerEvent : ISqEvent
{
    public byte Type { get; set; }
    public byte Value { get; set; }

    public void Read(BinaryStream bs)
    {
        Type = bs.Read1Byte();
        Value = bs.Read1Byte();

        // Supported general purpose types:
        // 1 (Modulation Wheel)
        // 2 (Breath)
        // 7 (Volume)
        // 10 (Pan)
        // 11 (Expression)
        // 64 (Hold Pedal #1) - 
        // 91 (Reverb Level)
        // 94 (Celeste Depth) - SDDRV::VoiceFilter::setDirectVolume
        // 102 (???? Custom?)

        // NOTE: SqSequencer supports NRPN (type 99) value 20 and 30
        // 20 is start of loop - saves current midi pointer,
        // 30 is end of loop. resumes midi pointer & jumps back to the start of the loop.
    }
}

public class SqProgramEvent : ISqEvent
{
    public byte Program { get; set; }
    public byte Value { get; set; }

    public void Read(BinaryStream bs)
    {
        Program = bs.Read1Byte();
    }
}

public class SqPitchBendEvent : ISqEvent
{
    public byte Lsb { get; set; }
    public void Read(BinaryStream bs)
    {
        Lsb = bs.Read1Byte();
    }
}

public class SqMetaEvent : ISqEvent
{
    public byte Type { get; set; }
    public uint Length { get; set; }
    public ISqMeta Meta { get; set; }

    public void Read(BinaryStream bs)
    {
        Type = bs.Read1Byte();
        Length = (uint)bs.Read7BitInt32();

        if (Type == 0x51)
        {
            Meta = new SqSetTempoEvent();
            Meta.Read(bs);
        }
        else
        {
            bs.ReadBytes((int)Length);
        }
    }
}

public interface ISqEvent
{
    public void Read(BinaryStream bs);
}
