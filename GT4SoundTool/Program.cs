using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Buffers.Binary;
using System.Runtime.InteropServices;

// DryWetMidi: For creating midi from sqt
using Melanchall.DryWetMidi;
using Melanchall.DryWetMidi.Multimedia;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Common;

// Kermalis.SoundFont2: For creating sound font from ins
using Kermalis.SoundFont2;

// MeltySynth: Synthesizer for getting audio out of midi and sf2 combined
// Note: added as git submodule for easier debugging
using MeltySynth;

// NAudio: Creating wav file from waveform output from MeltySynth
using NAudio.Wave;

using Syroot.BinaryData;

using GT4SoundTool.Formats;
using GT4SoundTool.Vag;
using GT4SoundTool.Formats.Jam;
using GT4SoundTool.Formats.Ssqt;
using GT4SoundTool.Formats.Ssqt.Meta;
using System.IO;

namespace GT4SoundTool;

public class Program
{
    // Quick note: some of audio is handled by IOP through PDISTR.IRX (PDI_Streaming_service)

    public static void Main(string[] args)
    {
        //ProcessES_INST(args[0]);
        ProcessSQT(args[0]);
    }

    private static void ProcessES_INST(string file)
    {
        var instrument = new InsHeader();
        var fs = new FileStream(file, FileMode.Open);
        instrument.Read(fs);

    }

    private static void ProcessSQT(string sqtFile)
    {
        int trackCount = 0;

        // We need the instrument header (note on real velocities are located there)
        string insPath = Path.ChangeExtension(sqtFile, ".ins");
        var instrument = new InsHeader();
        using (var fs = new FileStream(insPath, FileMode.Open))
        {
            instrument.Read(fs);

            // Sqt is just multiple midi tracks. Convert them to midi first.
            // Note: mid.inf is the list of available menu bgm files in the games. .mid extension is directly mentioned.
            trackCount = SsqtFileToMidi(sqtFile, instrument);
        }

        // Next, extract the sounds from the instrument file into a proper sound bank file
        InstrumentToSoundFont2(sqtFile, insPath);

        // Using each mid and the sound bank, synthetize and create a wav file.
        for (int i = 0; i < trackCount; i++)
        {
            string name = Path.GetFileNameWithoutExtension(sqtFile) + $".{i}.mid";
            Console.WriteLine($"Synthetizing {name}");

            SynthetizeMidiToWav(name, i);
        }
    }

    /// <summary>
    /// Converts a .ins (instrument)'s instruments to a sound font 2 file.
    /// </summary>
    /// <param name="path"></param>
    private static void InstrumentToSoundFont2(string sqtFile, string insFilePath)
    {
        // We need the sqt to find out which instrument should be percussion banks
        // Why? Because channel 10 (index 9) is treated differently SF2 wise
        var ssqt = new Ssqt();
        ssqt.Read(sqtFile);

        var instrument = new InsHeader();
        var fs = new FileStream(insFilePath, FileMode.Open);
        instrument.Read(fs);

        // We are creating one sf2 per track
        for (int i = 0; i < ssqt.Tracks.Count; i++)
        {
            List<(byte Channel, byte Program)> channelToPrograms = new List<(byte Channel, byte Program)>();
            foreach (SqMessage msg in ssqt.Tracks[i].Messages)
            {
                if (msg.Event is SqProgramEvent progChangeEvent)
                {
                    byte channel = (byte)(msg.Status & 0x0F);
                    channelToPrograms.Add((channel, progChangeEvent.Program));
                }
            }
            

            // Start by adding all the instruments/samples to the sf2
            var sf2 = new SF2();

            // First find all samples in the instrument file by navigating through program chunks
            Dictionary<uint, SampleInfo> vagSamples = new Dictionary<uint, SampleInfo>();

            int sampleIdx = 0;
            for (int j = 0; j < channelToPrograms.Count; j++)
            {
                JamProgChunk prog = instrument.JamHeader.ProgramChunks[channelToPrograms[j].Program];
                foreach (JamSplitChunk splitChunk in prog.SplitChunks)
                {
                    if ((byte)splitChunk.NoteMin == 0xFF & (byte)splitChunk.NoteMax == 0xFF)
                        continue;

                    // May refer to same sample so offset
                    if (vagSamples.ContainsKey(splitChunk.SD_VA_SSA))
                        continue;

                    fs.Position = instrument.EsHeader.DataOffset;

                    var bs = new BinaryStream(fs);
                    byte[] vag = splitChunk.GetData(bs, out uint loopStart, out uint loopEnd);

                    vagSamples.Add(splitChunk.SD_VA_SSA, new SampleInfo(vag, (ushort)vagSamples.Count));

                    // Decode sony vag format into regular waveform (PCM16)
                    byte[] decoded = SonyVag.Decode(vag);
                    Span<short> pcm16 = MemoryMarshal.Cast<byte, short>(decoded);

                    bool looping = loopStart != 0 && loopEnd != 0;

                    Console.WriteLine($"SF2: ins{j} (base note: {splitChunk.BaseNote}) - looping: {looping}");

                    uint wavLoopStart = 0;
                    if (looping)
                    {
                        double a = ((double)pcm16.Length / (vag.Length / 0x10));
                        wavLoopStart = (uint)(a * loopStart);
                    }

                    // Add the sample to the sound bank. Instruments will then pick which sample to use.
                    uint sampleId = sf2.AddSample(pcm16, $"sample{sampleIdx++}", looping, wavLoopStart, 44100, (byte)splitChunk.BaseNote, 0);

                    // Dump instrument noises (debug)
                    WaveFormat waveFormat = new WaveFormat(44100, 16, 1);
                    Directory.CreateDirectory("samples");
                    using (WaveFileWriter writer = new WaveFileWriter($"samples/instrument{j}_{splitChunk.BaseNote}.wav", waveFormat))
                        writer.WriteSamples(pcm16.ToArray(), 0, pcm16.Length);

                }
            }

            // If you need information, refer to the specification
            // https://www.synthfont.com/SFSPEC21.PDF

            // Essentially bags declare new incoming data for context (preset or instrument),
            // "generator" might sound complicated but it's a needlessly complicated name for a single parameter for either presets or instruments.

            for (int j = 0; j < channelToPrograms.Count; j++)
            {
                JamProgChunk prog = instrument.JamHeader.ProgramChunks[channelToPrograms[j].Program];

                string name = $"ch{channelToPrograms[j].Channel}_prog{channelToPrograms[j].Program}";

                if (channelToPrograms[j].Channel == 9) // Percussion channel
                    sf2.AddPreset(name, channelToPrograms[j].Program, 128);
                else
                    sf2.AddPreset(name, channelToPrograms[j].Program, 0);

                sf2.AddPresetBag();

                // Preset has a specified range with 0xFF (otherwise instruments provide it)
                //if (prog.CountOrFlag == 0xFF)
                //    sf2.AddPresetGenerator(SF2Generator.KeyRange, new SF2GeneratorAmount { LowByte = prog.FullRangeMin, HighByte = prog.FullRangeMax });

                sf2.AddPresetGenerator(SF2Generator.Instrument, new SF2GeneratorAmount { Amount = (short)sf2.AddInstrument(name) });

                for (int k = 0; k < prog.SplitChunks.Count; k++)
                {
                    var splitChunk = prog.SplitChunks[k];
                    if ((byte)splitChunk.NoteMin == 0xFF && (byte)splitChunk.NoteMax == 0xFF) // (note does not have data)
                        continue;

                    sf2.AddInstrumentBag();

                    int pan = (int)Normalize(splitChunk.Pan, 0, 128, -500, 500);
                    sf2.AddInstrumentGenerator(SF2Generator.Pan, new SF2GeneratorAmount { Amount = (short)pan });

                    //sf2.AddInstrumentGenerator(SF2Generator.CoarseTune, new SF2GeneratorAmount { Amount = (short)(splitChunk.UnkPitch)});

                    if (prog.CountOrFlag == 0xFF)
                        sf2.AddInstrumentGenerator(SF2Generator.KeyRange, new SF2GeneratorAmount { LowByte = (byte)(prog.StartNoteRange + k), HighByte = (byte)(prog.StartNoteRange + k) });
                    else
                        sf2.AddInstrumentGenerator(SF2Generator.KeyRange, new SF2GeneratorAmount { LowByte = (byte)prog.SplitChunks[k].NoteMin, HighByte = (byte)prog.SplitChunks[k].NoteMax });

                    sf2.AddInstrumentGenerator(SF2Generator.SampleID, new SF2GeneratorAmount { UAmount = vagSamples[splitChunk.SD_VA_SSA].SampleID });
                }
            }

            sf2.Save(Path.GetFileNameWithoutExtension(insFilePath) + $".{i}.sf2");
        }
    }

    public static double Normalize(double val, double valmin, double valmax, double min, double max)
    {
        return (((val - valmin) / (valmax - valmin)) * (max - min)) + min;
    }

    private static void SynthetizeMidiToWav(string fileName, int idx)
    {
        const int sampleRate = 44100;

        var midiFile = new MeltySynth.MidiFile(fileName);
        var synth = new MeltySynth.Synthesizer(fileName.Split(".")[0] + $".{idx}.sf2", sampleRate); // Use .sf2 to play the midi
        var sequencer = new MidiFileSequencer(synth); 
        sequencer.Play(midiFile, false);

        const int numChannels = 2; // It might sound like it's only mono, but it's still stereo, panning is pretty subtle
        int sampleLength = (int)(sampleRate * midiFile.Length.TotalSeconds);
        float[] samples = new float[sampleLength * numChannels];

        // Render the waveform.
        sequencer.RenderInterleaved(samples);

        WaveFormat waveFormat = new WaveFormat(sampleRate, 16, 2);
        using WaveFileWriter writer = new WaveFileWriter(Path.ChangeExtension(fileName, ".wav"), waveFormat);
        writer.WriteSamples(samples, 0, samples.Length);
    }

    private static int SsqtFileToMidi(string sqtFile, InsHeader instrumentHeader)
    {
        // Read the (s)sqt (midi-ish file)
        var ssqt = new Ssqt();
        ssqt.Read(sqtFile);

        // Converts sq events
        for (int i = 0; i < ssqt.Tracks.Count; i++)
        {
            var sqTrack = ssqt.Tracks[i];
            List<MidiEvent> midiEvents = new List<MidiEvent>(sqTrack.Messages.Count);

            int j = 0;
            foreach (SqMessage message in sqTrack.Messages)
            {
                if (message.Status == 0)
                    continue;

                MidiEvent midiEvent;
                if (message.Status == 0xFF)
                {
                    SqMetaEvent meta = message.Event as SqMetaEvent;
                    if (meta.Meta is SqSetTempoEvent tempoEvent)
                        midiEvent = new SetTempoEvent(tempoEvent.UsecPerQuarterNote);
                    else
                        throw new NotImplementedException();

                }
                else
                {
                    MidiEvent channelEvent;
                    if (message.Event is SqProgramEvent programEvent)
                    {
                        midiEvent = new ProgramChangeEvent((SevenBitNumber)programEvent.Program);
                    }
                    else if (message.Event is SqControllerEvent controllerEvent)
                    {
                        midiEvent = new ControlChangeEvent((SevenBitNumber)controllerEvent.Type, (SevenBitNumber)controllerEvent.Value);
                    }
                    else if (message.Event is SqPitchBendEvent pitchBendEvent)
                    {
                        // NOTE: Simplified to one byte in Sq
                        midiEvent = new PitchBendEvent((ushort)(pitchBendEvent.Lsb << 7));
                    }
                    else if (message.Event is SqNoteOnEvent noteOnEvent)
                    {
                        byte realVelocity = instrumentHeader.JamHeader.VelocityTable[noteOnEvent.Velocity];
                        midiEvent = new NoteOnEvent((SevenBitNumber)(byte)noteOnEvent.Note, (SevenBitNumber)noteOnEvent.Velocity);
                    }
                    else if (message.Event is SqNoteOffEvent noteOffEvent)
                    {
                        // NOTE: No velocity in Sq
                        midiEvent = new NoteOffEvent((SevenBitNumber)(byte)noteOffEvent.Note, (SevenBitNumber)0);
                    }
                    else
                    {
                        continue;
                    }


                    (midiEvent as ChannelEvent).Channel = (FourBitNumber)(message.Status & 0x0F);
                    j++;
                }

                midiEvent.DeltaTime = message.Delta;
                midiEvents.Add(midiEvent);
            }

            /* Don't really mind this, was to test figuring out BPM based on SDDRV::Sequencer::setMicroSecPerBeat (GT4O US: 0x5346B0)
             * us is from tempo events
            void SDDRV::Sequencer::setMicroSecPerBeat(Sequencer *this, int us)
            {
              float v2 = (float)us / (float)(1000000.0 / SDDRV::VoiceSystem::frame_rate_); // frame_rate_ is always 60.0 (not sure if different in non NTSC)
              float v3 = this->PPQN / v2;
              this->dword3C = 60000000.0 / v2;
              this->BPM = v3; // might not be bpm?
            } */

            var midi = new Melanchall.DryWetMidi.Core.MidiFile(new TrackChunk(midiEvents));
            midi.TimeDivision = new TicksPerQuarterNoteTimeDivision((short)sqTrack.TicksPerBeat); // Directly set the time division since we already have the raw value

            string fileName = Path.GetFileNameWithoutExtension(sqtFile);
            midi.Write($"{fileName}.{i}.mid", overwriteFile: true, MidiFileFormat.SingleTrack);
        }

        return ssqt.Tracks.Count;
    }
}

public record SampleInfo(byte[] SampleData, ushort SampleID);
