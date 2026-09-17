using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GT4SoundTool.Utils
{
    internal class ADSR
    {
        // everything here is taken from VGMtrans
        // only approximate and wont sound 100% like ps2
        public static short SecondsToTimecents(double seconds)
        {
            if (seconds < 0)
                return 0;

            if (double.IsInfinity(seconds))
                return 32767;

            if (seconds <= 0)
                return -32768;

            double tc = 1200.0 * Math.Log2(seconds);

            return (short)Math.Clamp(Math.Round(tc), short.MinValue, short.MaxValue);
        }
        public static short SustainLevelToCentibels(double level)
        {
            if (level <= 0)
                return 1440;

            if (level >= 1)
                return 0;

            double attenuationDb = -20.0 * Math.Log10(level);

            return (short)Math.Clamp(Math.Round(attenuationDb * 10.0), 0, 1440);
        }
        public sealed class PsxAdsrResult
        {
            public double AttackTime { get; set; }
            public double DecayTime { get; set; }
            public double SustainLevel { get; set; }
            public double SustainTime { get; set; }
            public double ReleaseTime { get; set; }
        }
        private static readonly uint[] AdsrRateTable = BuildAdsrRateTable();
        private static uint[] BuildAdsrRateTable()
        {
            var table = new uint[160];

            uint r = 3;
            uint rs = 1;
            uint rd = 0;

            for (int i = 32; i < 160; i++)
            {
                if (r < 0x3FFFFFFF)
                {
                    r += rs;
                    rd++;

                    if (rd == 5)
                    {
                        rd = 1;
                        rs *= 2;
                    }
                }

                if (r > 0x3FFFFFFF)
                    r = 0x3FFFFFFF;

                table[i] = r;
            }

            return table;
        }
        private static int RoundToZero(int value)
        {
            return value < 0 ? 0 : value;
        }
        private static uint RateAt(int index)
        {
            index = Math.Clamp(index, 0, AdsrRateTable.Length - 1);
            return AdsrRateTable[index];
        }
        private static PsxAdsrResult PsxConvAdsr(byte Am, byte Ar, byte Dr, byte Sl, byte Sm, byte Sd, byte Sr, byte Rm, byte Rr, bool bPS2)
        {

            if (Am > 1 || Ar > 0x7F || Dr > 0x0F || Sl > 0x0F || Rm > 1 || Rr > 0x1F || Sm > 1 || Sd > 1 || Sr > 0x7F)
            {
                throw new ArgumentOutOfRangeException(nameof(Ar), "PSX ADSR value out of range.");
            }

            // PS1 = 44.1kHz, PS2 = 48kHz.
            double sampleRate = bPS2 ? 48000.0 : 44100.0;

            int[] rateIncTable = { 0, 4, 6, 8, 9, 10, 11, 12 };

            long envelopeLevel;
            double samples;
            uint rate;
            uint remainder;
            double timeInSecs;
            int l;

            // ------------------------------------------------------------
            // Attack
            // ------------------------------------------------------------

            if ((Ar ^ 0x7F) < 0x10)
                Ar = 0;

            if (Am == 0)
            {
                rate = RateAt(RoundToZero((Ar ^ 0x7F) - 0x10) + 32);

                samples = Math.Ceiling(0x7FFFFFFF / (double)rate);
            }
            else
            {
                rate = RateAt(RoundToZero((Ar ^ 0x7F) - 0x10) + 32);
                samples = 0x60000000 / (double)rate;
                remainder = (uint)(0x60000000 % rate);
                rate = RateAt(RoundToZero((Ar ^ 0x7F) - 0x18) + 32);
                samples += Math.Ceiling(Math.Max(0, 0x1FFFFFFF - remainder) / (double)rate);
            }

            timeInSecs = samples / sampleRate;

            var result = new PsxAdsrResult
            {
                AttackTime = timeInSecs
            };

            // ------------------------------------------------------------
            // Decay
            // ------------------------------------------------------------

            envelopeLevel = 0x7FFFFFFF;
            bool sustainLevelFound = false;
            uint sustainLevel = 0;

            for (l = 0; envelopeLevel > 0; l++)
            {
                if (4 * (Dr ^ 0x1F) < 0x18)
                    Dr = 0;

                int rateOffset;

                switch ((envelopeLevel >> 28) & 0x7)
                {
                    case 0:
                        rateOffset =
                            4 * (Dr ^ 0x1F) - 0x18 + 0;
                        break;

                    case 1:
                        rateOffset =
                            4 * (Dr ^ 0x1F) - 0x18 + 4;
                        break;

                    case 2:
                        rateOffset =
                            4 * (Dr ^ 0x1F) - 0x18 + 6;
                        break;

                    case 3:
                        rateOffset =
                            4 * (Dr ^ 0x1F) - 0x18 + 8;
                        break;

                    case 4:
                        rateOffset =
                            4 * (Dr ^ 0x1F) - 0x18 + 9;
                        break;

                    case 5:
                        rateOffset =
                            4 * (Dr ^ 0x1F) - 0x18 + 10;
                        break;

                    case 6:
                        rateOffset =
                            4 * (Dr ^ 0x1F) - 0x18 + 11;
                        break;

                    default:
                        rateOffset =
                            4 * (Dr ^ 0x1F) - 0x18 + 12;
                        break;
                }

                rate = RateAt(RoundToZero(rateOffset) + 32);
                envelopeLevel -= rate;

                if (!sustainLevelFound && (((envelopeLevel >> 27) & 0xF) <= Sl))
                {
                    sustainLevel = unchecked((uint)envelopeLevel);
                    sustainLevelFound = true;
                }
            }

            samples = l;
            timeInSecs = samples / sampleRate;
            result.DecayTime = timeInSecs;

            // ------------------------------------------------------------
            // Sustain rate
            // ------------------------------------------------------------

            envelopeLevel = 0x7FFFFFFF;

            // Increasing sustain rate is not represented in SF2.
            if (Sd == 0)
            {
                result.SustainTime = -1;
            }
            else if (Sr == 0x7F)
            {
                // Infinite.
                result.SustainTime = -1;
            }
            else
            {
                if (Sm == 0)
                {
                    // Linear
                    rate = RateAt(RoundToZero((Sr ^ 0x7F) - 0x0F) + 32);

                    samples = Math.Ceiling(0x7FFFFFFF / (double)rate);
                }
                else
                {
                    // Exponential
                    l = 0;

                    while (envelopeLevel > 0)
                    {
                        long envelopeLevelDiff;
                        long envelopeLevelTarget;

                        switch ((envelopeLevel >> 28) & 0x7)
                        {
                            case 0:
                                envelopeLevelTarget = 0x00000000;
                                envelopeLevelDiff = RateAt(RoundToZero((Sr ^ 0x7F) - 0x1B + 0) + 32);
                                break;

                            case 1:
                                envelopeLevelTarget = 0x0FFFFFFF;
                                envelopeLevelDiff = RateAt(RoundToZero((Sr ^ 0x7F) - 0x1B + 4) + 32);
                                break;

                            case 2:
                                envelopeLevelTarget = 0x1FFFFFFF;
                                envelopeLevelDiff = RateAt(RoundToZero((Sr ^ 0x7F) - 0x1B + 6) + 32);
                                break;

                            case 3:
                                envelopeLevelTarget = 0x2FFFFFFF;
                                envelopeLevelDiff = RateAt(RoundToZero((Sr ^ 0x7F) - 0x1B + 8) + 32);
                                break;

                            case 4:
                                envelopeLevelTarget = 0x3FFFFFFF;
                                envelopeLevelDiff = RateAt(RoundToZero((Sr ^ 0x7F) - 0x1B + 9) + 32);
                                break;

                            case 5:
                                envelopeLevelTarget = 0x4FFFFFFF;
                                envelopeLevelDiff = RateAt(RoundToZero((Sr ^ 0x7F) - 0x1B + 10) + 32);
                                break;

                            case 6:
                                envelopeLevelTarget = 0x5FFFFFFF;
                                envelopeLevelDiff = RateAt(RoundToZero((Sr ^ 0x7F) - 0x1B + 11) + 32);
                                break;

                            default:
                                envelopeLevelTarget = 0x6FFFFFFF;
                                envelopeLevelDiff = RateAt(RoundToZero((Sr ^ 0x7F) - 0x1B + 12) + 32);
                                break;
                        }

                        long steps = (envelopeLevel - envelopeLevelTarget + (envelopeLevelDiff - 1)) / envelopeLevelDiff;
                        envelopeLevel -= envelopeLevelDiff * steps;
                        l += checked((int)steps);
                    }

                    samples = l;
                }

                timeInSecs = samples / sampleRate;

                result.SustainTime = LinAmpDecayTimeToLinDbDecayTime(timeInSecs, 0x800);
            }

            // ------------------------------------------------------------
            // Sustain level
            // ------------------------------------------------------------


            if (Sl == 0)
                sustainLevel = 0x07FFFFFF;

            result.SustainLevel =
                sustainLevel / (double)0x7FFFFFFF;

            // ------------------------------------------------------------
            // Sustain-rate workaround
            // ------------------------------------------------------------

            if ((result.DecayTime < 2 || (Dr == 0x0F && Sl >= 0x0C)) && Sr < 0x7E && Sd == 1)
            {
                result.SustainLevel = 0;
                result.DecayTime = result.SustainTime;
            }

            // ------------------------------------------------------------
            // Release
            // ------------------------------------------------------------

            envelopeLevel = 0x7FFFFFFF;

            if (Rm == 0)
            {
                // Linear
                rate = RateAt(RoundToZero((4 * (Rr ^ 0x1F)) - 0x0C) + 32);

                if (rate != 0)
                {
                    samples = Math.Ceiling(envelopeLevel / (double)rate);
                }
                else
                {
                    samples = 0;
                }
            }
            else
            {
                // Exponential
                if (((Rr ^ 0x1F) * 4) < 0x18)
                    Rr = 0;

                for (l = 0; envelopeLevel > 0; l++)
                {
                    int rateOffset;

                    switch ((envelopeLevel >> 28) & 0x7)
                    {
                        case 0:
                            rateOffset = 4 * (Rr ^ 0x1F) - 0x18 + 0;
                            break;

                        case 1:
                            rateOffset = 4 * (Rr ^ 0x1F) - 0x18 + 4;
                            break;

                        case 2:
                            rateOffset = 4 * (Rr ^ 0x1F) - 0x18 + 6;
                            break;

                        case 3:
                            rateOffset = 4 * (Rr ^ 0x1F) - 0x18 + 8;
                            break;

                        case 4:
                            rateOffset = 4 * (Rr ^ 0x1F) - 0x18 + 9;
                            break;

                        case 5:
                            rateOffset = 4 * (Rr ^ 0x1F) - 0x18 + 10;
                            break;

                        case 6:
                            rateOffset = 4 * (Rr ^ 0x1F) - 0x18 + 11;
                            break;

                        default:
                            rateOffset = 4 * (Rr ^ 0x1F) - 0x18 + 12;
                            break;
                    }

                    envelopeLevel -= RateAt(RoundToZero(rateOffset) + 32);
                }

                samples = l;
            }

            timeInSecs = samples / sampleRate;
            result.ReleaseTime = LinAmpDecayTimeToLinDbDecayTime(timeInSecs, 0x800);

            return result;
        }
        private static double LinAmpDecayTimeToLinDbDecayTime(double timeInSeconds, int dbDrop)
        {
            if (timeInSeconds <= 0)
                return 0;

            return timeInSeconds * 5.0;
        }
        public static PsxAdsrResult ConvertGt4Adsr(short adsr1Value, short adsr2Value)
        {
            ushort adsr1 = unchecked((ushort)adsr1Value);
            ushort adsr2 = unchecked((ushort)adsr2Value);

            byte am = (byte)((adsr1 & 0x8000) >> 15);
            byte ar = (byte)((adsr1 & 0x7F00) >> 8);
            byte dr = (byte)((adsr1 & 0x00F0) >> 4);
            byte sl = (byte)(adsr1 & 0x000F);

            byte sm = (byte)((adsr2 & 0x8000) >> 15);
            byte sd = (byte)((adsr2 & 0x4000) >> 14);
            byte sr = (byte)((adsr2 >> 6) & 0x7F);
            byte rm = (byte)((adsr2 & 0x0020) >> 5);
            byte rr = (byte)(adsr2 & 0x001F);

            return PsxConvAdsr(am, ar, dr, sl, sm, sd, sr, rm, rr, true);
        }
    }
}
