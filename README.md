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
I wrote this emulator in order to learn C# and .NET, as well as more about the NES hardware. 
In this implementation:
- Raylib is used to display the pixel buffer produced by the PPU, and to get user input.
- Only the iNES ROM format is supported. 
- Mappers 000-004 are fully implemented. This accounts for ~80% of NES games.
- Games with battery backed saves will save a file containing SRAM state alongside the ROM file on quit. This file is then read again next time the ROM is loaded allowing for game saving.
