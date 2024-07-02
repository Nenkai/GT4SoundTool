
# GT4SoundTool

### UNFINISHED

GT4 tool for conversion of music banks into midi/sound fonts.

Current state:
* Can extract MIDIs out of `.sqt` (Sq table)
* Can somewhat extract sound samples (vag format) from `.ins` (instrument) files into `.sf2` sound banks
* Can create `.wav` from a midi & sf2 (won't be too accurate, some parameters are missing & some things need to be fixed)
* Again, the wav output is **not** currently accurate enough
* Will **NOT** currently read `SeSeq` (sound effect "midi" sequences) embedded inside instrument files, but refer to this repo for structures (they are figured)

---

**This repo should be considered as a small non-serious test project to research sound files, perhaps someone can pick up on it in the future.**

The documentation is the code. I've documented my references as much as I could.

## Documentation

There are two types of sequencers to keep in mind, `Sq` and `Se`, `Sq` for tracks, `Se` for sound effects.

#### Sq
`Sq` (file magic `Ssqt` & `Sssq`), used by `.sqt` files, is quite simply a midi file that can contain multiple sequences aka tracks. The format is nearly identical to the [MIDI File Format](https://www.music.mcgill.ca/~ich/classes/mumt306/StandardMIDIfileformat.html).

`SDDRV::SqSequencer::*` handles these.

#### Ins (Instrument)
`.ins` (file magic `INST`) files are linked to those `Sq`'s, providing the samples for each **ins**trument (& their note ranges) used by each MIDI channel. Despite having `SShd` header within, **these files does not follow the regular/general Sony `SShd/SSbd` header format normally used by `.ads` music formats**.

`Ins` is also refered to as ***Jam***, possibly directly referencing the sound authoring tool provided by the PS2 SDK (in `PS2SDK/P-sound/atools/Sndtool111/mac/SoundPreview111/Doc/html/jam.htm` - note: documentation is all in japanese).

`Ins` are also used **standalone** for *sound effect* banks where their MIDI sequences are directly bundled in, these are `Se`'s, handled by `SDDRV::SeSequencer::*` with a different set of supported MIDI commands. 

General structure (actual field layout omitted):

* `InsHeader` - General Instrument Header (`carsound` files also uses this exact structure with a different magic for a totally different purpose)
  * `EsHeader`
    * `JamHeader`
      * `JamProgramChunk` - Chunks holding programs for each midi channel (when `ProgramChange` is called) with details such as note range & more
        * `JamSplitChunk` - Chunk per note range with details such as note range, PS2 ADSR settings, volume, pan, lfo table index & more
      * `VelocityChunk` - Chunk holding note on velocity - velocities are indexed into this to get the real one
      * `LfoTableChunk` - Lfo Param Chunk
      * `UnkChunk`
      * `SeSeqChunk` - MIDI Sequences for the `Se` Sequencer (sound effects)
      * `SeProgChunk` - Holds programs for each midi channel for the `Se` Sequencer (sound effects)

### Current State/Problems/Reverse-Engineering Notes

Currently, this tool takes `.sqt` files and spits out:
* `.mid` - One MIDI sequence per track inside `.sqt`
* `.sf2` - One SoundFont2 per track with the instruments
* `.wav` - A stereo wav conversion - just an attempt.
* `samples` folder - Extracted samples from `.ins`, mono

The `.wav` is partially correct. But:
* The pitch for certain notes is incorrect. This is caused by the fourth byte in `JamSplitChunk` not currently being used - forcing the game not to apply it confirms that the same overall incorrect pitch happens so this is a good sign
* Some notes aren't held long enough. Not sure why.
* Volume provided by `JamSplitChunk`s isn't really accounted for yet
* ADSR Envelope values in `JamSplitChunk` aren't accounted for yet, not sure how to interpret it. [vgmtrans's PSXSPU.h](https://github.com/vgmtrans/vgmtrans/blob/6f6f86823ab10ce72f0c6acd6ef7991e631613f7/src/main/formats/common/PSXSPU.h#L128) might give a clue, but one of the issues mentions that not [everything is inherently supported by `.sf2`](https://github.com/vgmtrans/vgmtrans/issues/138).
* Loops. `ins` holds sample data as the usual Sony `.vag` format which explicitly has loop data. I've tried to properly detect loops so that they can be written into the `.sf2` properly, but samples don't quite loop properly yet.
* Lack of reverb - Flag `0x80` within `JamSplitChunk` enables mixing/reverb, not sure how to handle it.
* No LFO handling
---

As for reverse-engineering, the PS2's EE cannot interact directly with the sound chip so everything has to go through IOP first. While it is possible to use built-in libs that only lets you focus on EE code, the game does this pretty low level (using libsd). 

It essentially goes like this internally:

-> Init SDDRV (Sequencers, Voice System, Es, etc).
-> Start `SPUP` RPC Service (`PDISPU2.irx`) 
-> Open files i.e `SDDRV::Jam::open`
-> Process reading sqt, etc.
-> `SDDRV::VoiceSystem::updateCore` passes SPU structure containing sound parameters to IOP
-> `SPUP Rpc Cmd 4` to pass said structure
-> [In IOP] `sceSdSetAddr`, `sceSdSetParam`, `sceSdSetSwitch` etc using structure.

#### Useful Tools/Docs

* [Midica](https://www.midica.org/) - Playback of .mid + .sf2, seeing events
* [Polyphone](https://www.polyphone-soundfonts.com/) - Debugging of .sf2
* [SF2 Specification](https://www.synthfont.com/SFSPEC21.PDF)
* IDA
* PS2 SDK Docs, like PS2 IOP Reference Sound, sdmacro.h, etc
* IDA databases to be provided at a later date
* **The code for this tool** will have more details.
