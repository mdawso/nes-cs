# NES-CS

## Quick Start
```bash
dotnet run -- <path-to-rom>
```

## Controls 
- Z, X = A, B
- Enter, RShift = Start, Select
- Arrow Keys = DPad

## About
I wrote this emulator in order to learn more about C#, .NET and the NES hardware. 
In this implementation:
- Raylib is used to display the pixel buffer produced by the PPU, and to get user input.
- Only the iNES ROM format is supported.
- As long as it is compiled in the iNES format, the emulator is able to run arbitrary code compiled for the 6502 CPU. Tested with [cc65](https://github.com/cc65/cc65) (C & ASM).
- Mappers 000-004 are fully implemented. This accounts for ~80% of NES games.
- Games with battery backed saves will save a file containing SRAM state alongside the ROM file on quit. This file is then read again next time the ROM is loaded allowing for game saving.
