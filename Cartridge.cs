using System;
using System.IO;

namespace nes;

public class Cartridge
{
    private byte[] _prgMemory;
    private byte[] _chrMemory;
    public byte[] PrgRam = new byte[8192];

    private byte _mapperId;
    private byte _prgBanks;
    private byte _chrBanks;

    private bool _verticalMirroring;

    public bool VerticalMirroring => _verticalMirroring;
    public byte MapperId => _mapperId;
    public byte PrgBanks => _prgBanks;
    public byte ChrBanks => _chrBanks;

    public Mapper? Mapper { get; set; }

    public Cartridge(string fileName)
    {
        using FileStream fs = new FileStream(fileName, FileMode.Open, FileAccess.Read);
        using BinaryReader br = new BinaryReader(fs);

        byte[] header = br.ReadBytes(16);

        if (header[0] != 'N' || header[1] != 'E' || header[2] != 'S' || header[3] != 0x1A)
            throw new Exception("Invalid iNES file format.");

        _prgBanks = header[4];
        _chrBanks = header[5];

        _mapperId = (byte)((header[7] & 0xF0) | (header[6] >> 4));
        _verticalMirroring = (header[6] & 0x01) != 0;

        if ((header[6] & 0x04) != 0)
            br.ReadBytes(512);

        _prgMemory = br.ReadBytes(_prgBanks * 16384);

        if (_chrBanks == 0)
            _chrMemory = new byte[8192];
        else
            _chrMemory = br.ReadBytes(_chrBanks * 8192);
    }

    public byte ReadPrg(uint mappedAddr)
    {
        // 0x80000000 bit signals that this is a PRG RAM address
        if ((mappedAddr & 0x80000000) != 0)
            return PrgRam[mappedAddr & 0x1FFF];

        return _prgMemory[mappedAddr];
    }

    public void WritePrg(uint mappedAddr, byte data)
    {
        if ((mappedAddr & 0x80000000) != 0)
            PrgRam[mappedAddr & 0x1FFF] = data;
        else
            _prgMemory[mappedAddr] = data;
    }

    public byte ReadChr(uint mappedAddr)
    {
        return _chrMemory[mappedAddr];
    }

    public void WriteChr(uint mappedAddr, byte data)
    {
        _chrMemory[mappedAddr] = data;
    }

    public bool PpuRead(ushort addr, out byte data)
    {
        data = 0;
        if (Mapper != null && Mapper.MapPpuRead(addr, out uint mappedAddr))
        {
            data = _chrMemory[mappedAddr];
            return true;
        }
        return false;
    }

    public bool PpuWrite(ushort addr, byte data)
    {
        if (Mapper != null && Mapper.MapPpuWrite(addr, out uint mappedAddr))
        {
            _chrMemory[mappedAddr] = data;
            return true;
        }
        return false;
    }
}