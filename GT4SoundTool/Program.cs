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

using GT4SqTest.Formats;
using GT4SqTest.Vag;
using GT4SqTest.Formats.Jam;
using GT4SqTest.Formats.Ssqt;
using GT4SqTest.Formats.Ssqt.Meta;
using System.IO;

namespace GT4SqTest
{
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

        private static void ProcessSQT(string file)
        {
            // Sqt is just multiple midi tracks. Convert them to midi first.
            // Note: mid.inf is the list of available menu bgm files in the games. .mid extension is directly mentioned.
            int trackCount = SsqtToMidi(file);

            // Next, extract the sounds from the instrument file into a proper sound bank file
            InstrumentToSoundFont2(file);

            // Using each mid and the sound bank, synthetize and create a wav file.
            for (int i = 0; i < trackCount; i++)
            {
                string name = Path.GetFileNameWithoutExtension(file) + $".{i}.mid";
                Console.WriteLine($"Synthetizing {name}");

                SynthetizeMidiToWav(name);
            }
        }

        /// <summary>
        /// Converts a .ins (instrument)'s instruments to a sound font 2 file.
        /// </summary>
        /// <param name="path"></param>
        private static void InstrumentToSoundFont2(string path)
        {
            var sf2 = new SF2();
            var instrument = new InsHeader();
            var fs = new FileStream(Path.ChangeExtension(path, ".ins"), FileMode.Open);
            instrument.Read(fs);

            // First find all samples in the instrument file by navigating through program chunks
            Dictionary<uint, SampleInfo> vagSamples = new Dictionary<uint, SampleInfo>();
            int i = 0;
            foreach (JamProgChunk prog in instrument.JamHeader.ProgramChunks)
            {
                foreach (var splitChunk in prog.SplitChunks)
                {
                    if (splitChunk.NoteMin == 0xFF & splitChunk.NoteMax == 0xFF)
                        continue;

                    // May refer to same sample so offset
                    if (vagSamples.ContainsKey(splitChunk.SsaOffset))
                        continue;

                    fs.Position = instrument.EsHeader.DataOffset;

                    var bs = new BinaryStream(fs);
                    byte[] vag = splitChunk.GetData(bs);

                    vagSamples.Add(splitChunk.SsaOffset, new SampleInfo(vag, (ushort)vagSamples.Count));

                    // Decode sony vag format into regular waveform (PCM16)
                    byte[] decoded = SonyVag.Decode(vag);
                    Span<short> pcm16 = MemoryMarshal.Cast<byte, short>(decoded);

                    // Add the sample to the sound bank. Instruments will then pick which sample to use.
                    uint sampleId = sf2.AddSample(pcm16, $"sample{i}", true, 0, 44100, splitChunk.BaseNoteMaybe, 0); // Not 100% sure about base note

                    // Dump instrument noises (debug)
                    WaveFormat waveFormat = new WaveFormat(44100, 16, 1);
                    Directory.CreateDirectory("samples");
                    using (WaveFileWriter writer = new WaveFileWriter($"samples/instrument{i}.wav", waveFormat))
                        writer.WriteSamples(pcm16.ToArray(), 0, pcm16.Length);

                    i++;
                }
            }

            // If you need information, refer to the specification
            // https://www.synthfont.com/SFSPEC21.PDF

            // Essentially bags declare new incoming data for context (preset or instrument),
            // "generator" might sound complicated but it's a needlessly complicated name for a single parameter for either presets or instruments.

            i = 0;
            foreach (var prog in instrument.JamHeader.ProgramChunks)
            {
                string name = $"Instrument{i}";
                sf2.AddPreset(name, (ushort)i, 0);
                sf2.AddPresetBag();

                // Preset has a specified range with 0xFF (otherwise instruments provide it)
                if (prog.CountOrFlag == 0xFF)
                    sf2.AddPresetGenerator(SF2Generator.KeyRange, new SF2GeneratorAmount { LowByte = prog.FullRangeMin, HighByte = prog.FullRangeMax });

                sf2.AddPresetGenerator(SF2Generator.Instrument, new SF2GeneratorAmount { Amount = (short)sf2.AddInstrument(name) });

                for (int j = 0; j < prog.SplitChunks.Count; j++)
                {
                    var splitChunk = prog.SplitChunks[j];
                    if (splitChunk.NoteMin == 0xFF && splitChunk.NoteMax == 0xFF) // (note does not have data)
                        continue;

                    sf2.AddInstrumentBag();

                    if ((splitChunk.Flags & 0x80) != 0)
                        sf2.AddInstrumentGenerator(SF2Generator.ReverbEffectsSend, new SF2GeneratorAmount { Amount = 200 }); // TODO: correct amount

                    if (prog.CountOrFlag == 0xFF)
                        sf2.AddInstrumentGenerator(SF2Generator.KeyRange, new SF2GeneratorAmount { LowByte = (byte)(prog.FullRangeMin + j), HighByte = (byte)(prog.FullRangeMin + j) });
                    else
                        sf2.AddInstrumentGenerator(SF2Generator.KeyRange, new SF2GeneratorAmount { LowByte = prog.SplitChunks[j].NoteMin, HighByte = prog.SplitChunks[j].NoteMax });

                    sf2.AddInstrumentGenerator(SF2Generator.SampleID, new SF2GeneratorAmount { UAmount = vagSamples[splitChunk.SsaOffset].SampleID });
                }

                i++;
            }

            sf2.Save(Path.GetFileNameWithoutExtension(path) + ".sf2");
        }

        private static void SynthetizeMidiToWav(string fileName)
        {
            var sampleRate = 44100;
            var midiFile = new MeltySynth.MidiFile(fileName);
            var synth = new MeltySynth.Synthesizer(fileName.Split(".")[0] + ".sf2", sampleRate); // Use .sf2 to play the midi
            var sequencer = new MidiFileSequencer(synth); 
            sequencer.Play(midiFile, false);

            // Sq is always mono.
            var mono = new float[(int)(sampleRate * midiFile.Length.TotalSeconds)];

            // Render the waveform.
            sequencer.RenderMono(mono);

            WaveFormat waveFormat = new WaveFormat(sampleRate, 16, 1);
            using (WaveFileWriter writer = new WaveFileWriter(Path.ChangeExtension(fileName, ".wav"), waveFormat))
            {
                writer.WriteSamples(mono, 0, mono.Length);
            }
        }

        private static int SsqtToMidi(string file)
        {
            var ssqt = new Ssqt();
            ssqt.Read(file);

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
                            midiEvent = new NoteOnEvent((SevenBitNumber)noteOnEvent.Note, (SevenBitNumber)noteOnEvent.Velocity);
                        }
                        else if (message.Event is SqNoteOffEvent noteOffEvent)
                        {
                            // NOTE: No velocity in Sq
                            midiEvent = new NoteOffEvent((SevenBitNumber)noteOffEvent.Note, (SevenBitNumber)0);
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

                string fileName = Path.GetFileNameWithoutExtension(file);
                midi.Write($"{fileName}.{i}.mid", overwriteFile: true, MidiFileFormat.SingleTrack);
            }

            return ssqt.Tracks.Count;
        }
    }

    public record SampleInfo(byte[] SampleData, ushort SampleID);
}
