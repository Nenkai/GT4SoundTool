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

**This repo should be used as proof of work, perhaps someone can pick up on it in the future.**

The documentation is the code. I've documented my references as much as I could.
