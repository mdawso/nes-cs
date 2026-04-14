using System;

namespace nes;

public enum MirrorMode
{
    Hardware,
    Horizontal,
    Vertical,
    SingleScreenLower,
    SingleScreenUpper
}

public abstract class Mapper
{
    protected byte prgBanks;
    protected byte chrBanks;

    public abstract MirrorMode MirrorMode { get; }

    public Mapper(byte prgBanks, byte chrBanks)
    {
        this.prgBanks = prgBanks;
        this.chrBanks = chrBanks;
    }

    public abstract bool MapCpuRead(ushort addr, out uint mappedAddr);
    public abstract bool MapCpuWrite(ushort addr, out uint mappedAddr, byte data = 0);
    public abstract bool MapPpuRead(ushort addr, out uint mappedAddr);
    public abstract bool MapPpuWrite(ushort addr, out uint mappedAddr);
}

public class Mapper000 : Mapper
{
    public Mapper000(byte prgBanks, byte chrBanks) : base(prgBanks, chrBanks) { }

    public override MirrorMode MirrorMode => MirrorMode.Hardware; 

    public override bool MapCpuRead(ushort addr, out uint mappedAddr)
    {
        mappedAddr = 0;
        if (addr >= 0x8000)
        {
            mappedAddr = (uint)(addr & (prgBanks > 1 ? 0x7FFF : 0x3FFF));
            return true;
        }
        return false;
    }

    public override bool MapCpuWrite(ushort addr, out uint mappedAddr, byte data = 0)
    {
        mappedAddr = 0;
        if (addr >= 0x8000)
        {
            mappedAddr = (uint)(addr & (prgBanks > 1 ? 0x7FFF : 0x3FFF));
            return true;
        }
        return false;
    }

    public override bool MapPpuRead(ushort addr, out uint mappedAddr)
    {
        mappedAddr = 0;
        if (addr <= 0x1FFF)
        {
            mappedAddr = addr;
            return true;
        }
        return false;
    }

    public override bool MapPpuWrite(ushort addr, out uint mappedAddr)
    {
        mappedAddr = 0;
        if (addr <= 0x1FFF && chrBanks == 0)
        {
            mappedAddr = addr;
            return true;
        }
        return false;
    }
}

public class Mapper001 : Mapper
{
    private byte _shiftRegister = 0x10;
    private byte _controlRegister = 0x1C;
    private byte _chrBank0 = 0x00;
    private byte _chrBank1 = 0x00;
    private byte _prgBank = 0x00;

    private uint _prgBankOffset0;
    private uint _prgBankOffset1;
    private uint _chrBankOffset0;
    private uint _chrBankOffset1;

    public override MirrorMode MirrorMode
    {
        get
        {
            int mode = _controlRegister & 0x03;
            return mode switch
            {
                0 => MirrorMode.SingleScreenLower,
                1 => MirrorMode.SingleScreenUpper,
                2 => MirrorMode.Vertical,
                3 => MirrorMode.Horizontal,
                _ => MirrorMode.Hardware
            };
        }
    }

    public Mapper001(byte prgBanks, byte chrBanks) : base(prgBanks, chrBanks)
    {
        UpdateOffsets();
    }

    public override bool MapCpuRead(ushort addr, out uint mappedAddr)
    {
        mappedAddr = 0;
        if (addr >= 0x6000 && addr <= 0x7FFF)
        {
            mappedAddr = 0x80000000 | (uint)(addr & 0x1FFF); // Highest bit set indicates prg ram instead of prg rom.
            return true;
        }
        if (addr >= 0x8000 && addr <= 0xBFFF)
        {
            mappedAddr = _prgBankOffset0 + (uint)(addr & 0x3FFF);
            return true;
        }
        if (addr >= 0xC000 && addr <= 0xFFFF)
        {
            mappedAddr = _prgBankOffset1 + (uint)(addr & 0x3FFF);
            return true;
        }
        return false;
    }

    public override bool MapCpuWrite(ushort addr, out uint mappedAddr, byte data = 0)
    {
        mappedAddr = 0;

        if (addr >= 0x6000 && addr <= 0x7FFF)
        {
            mappedAddr = 0x80000000 | (uint)(addr & 0x1FFF);
            return true;
        }

        if (addr >= 0x8000)
        {
            if ((data & 0x80) != 0)
            {
                _shiftRegister = 0x10;
                _controlRegister |= 0x0C;
                UpdateOffsets();
            }
            else
            {
                bool complete = (_shiftRegister & 1) != 0;
                _shiftRegister >>= 1;
                _shiftRegister |= (byte)((data & 1) << 4);

                if (complete)
                {
                    byte target = (byte)((addr >> 13) & 0x03);
                    if (target == 0) // 0x8000 - 0x9FFF
                        _controlRegister = _shiftRegister;
                    else if (target == 1) // 0xA000 - 0xBFFF
                        _chrBank0 = _shiftRegister;
                    else if (target == 2) // 0xC000 - 0xDFFF
                        _chrBank1 = _shiftRegister;
                    else if (target == 3) // 0xE000 - 0xFFFF
                        _prgBank = _shiftRegister;

                    _shiftRegister = 0x10;
                    UpdateOffsets();
                }
            }
            return false;
        }

        return false;
    }

    public override bool MapPpuRead(ushort addr, out uint mappedAddr)
    {
        mappedAddr = 0;
        if (addr <= 0x0FFF)
        {
            mappedAddr = _chrBankOffset0 + addr;
            return true;
        }
        if (addr >= 0x1000 && addr <= 0x1FFF)
        {
            mappedAddr = _chrBankOffset1 + (uint)(addr & 0x0FFF);
            return true;
        }
        return false;
    }

    public override bool MapPpuWrite(ushort addr, out uint mappedAddr)
    {
        mappedAddr = 0;
        if (addr <= 0x1FFF)
        {
            if (chrBanks == 0)
            {
                mappedAddr = addr; // Wrap as chr ram
                return true;
            }
            return true;
        }
        return false;
    }

    private void UpdateOffsets()
    {
        uint totalPrgMemory = (uint)(prgBanks * 16384);
        uint totalChrMemory = (uint)(Math.Max((byte)1, chrBanks) * 8192);

        byte prgMode = (byte)((_controlRegister >> 2) & 0x03);
        if (prgMode <= 1) // 32KB mode
        {
            uint bank = (uint)((_prgBank & 0x0E) >> 1);
            _prgBankOffset0 = (bank * 0x8000) % totalPrgMemory;
            _prgBankOffset1 = (_prgBankOffset0 + 0x4000) % totalPrgMemory;
        }
        else if (prgMode == 2) // Fix first, switch second
        {
            _prgBankOffset0 = 0;
            _prgBankOffset1 = ((uint)(_prgBank & 0x0F) * 0x4000) % totalPrgMemory;
        }
        else if (prgMode == 3) // Switch first, fix second
        {
            _prgBankOffset0 = ((uint)(_prgBank & 0x0F) * 0x4000) % totalPrgMemory;
            _prgBankOffset1 = ((uint)(prgBanks - 1) * 0x4000) % totalPrgMemory;
        }

        byte chrMode = (byte)((_controlRegister >> 4) & 0x01);
        if (chrMode == 0) // 8KB
        {
            uint bank = (uint)(_chrBank0 & 0x1E) >> 1;
            _chrBankOffset0 = (bank * 0x2000) % totalChrMemory;
            _chrBankOffset1 = (_chrBankOffset0 + 0x1000) % totalChrMemory;
        }
        else // 4KB
        {
            _chrBankOffset0 = ((uint)_chrBank0 * 0x1000) % totalChrMemory;
            _chrBankOffset1 = ((uint)_chrBank1 * 0x1000) % totalChrMemory;
        }
    }
}