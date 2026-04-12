using System;

namespace nes;

public class Cartridge
{
    private byte[] _prgMemory;
    private byte[] _chrMemory;
    private byte _mapperId;
    private byte _prgBanks;
    private byte _chrBanks;

    private bool _verticalMirroring;

    public bool VerticalMirroring => _verticalMirroring;

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

    public bool CpuRead(ushort addr, out byte data)
    {
        data = 0;
        if (addr >= 0x8000 && addr <= 0xFFFF)
        {
            ushort mappedAddr = _prgBanks > 1 ? (ushort)(addr & 0x7FFF) : (ushort)(addr & 0x3FFF);
            data = _prgMemory[mappedAddr];
            return true;
        }
        return false;
    }

    public bool CpuWrite(ushort addr, byte data)
    {
        if (addr >= 0x8000 && addr <= 0xFFFF)
        {
            return true;
        }
        return false;
    }

    public bool PpuRead(ushort addr, out byte data)
    {
        data = 0;
        if (addr >= 0x0000 && addr <= 0x1FFF)
        {
            data = _chrMemory[addr];
            return true;
        }
        return false;
    }

    public bool PpuWrite(ushort addr, byte data)
    {
        if (addr >= 0x0000 && addr <= 0x1FFF)
        {
            if (_chrBanks == 0)
            {
                _chrMemory[addr] = data;
                return true;
            }
            return true;
        }
        return false;
    }
}

