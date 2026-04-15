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

    public bool irqActive = false;

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

    public virtual void Scanline() { }
    public virtual void ClearIrq() { irqActive = false; }
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

public class Mapper002 : Mapper
{
    private byte _prgBankSelect = 0;

    public Mapper002(byte prgBanks, byte chrBanks) : base(prgBanks, chrBanks) { }

    public override MirrorMode MirrorMode => MirrorMode.Hardware;

    public override bool MapCpuRead(ushort addr, out uint mappedAddr)
    {
        mappedAddr = 0;
        if (addr >= 0x8000 && addr <= 0xBFFF)
        {
            mappedAddr = (uint)(_prgBankSelect * 0x4000) + (uint)(addr & 0x3FFF);
            return true;
        }
        if (addr >= 0xC000 && addr <= 0xFFFF)
        {
            mappedAddr = (uint)((prgBanks - 1) * 0x4000) + (uint)(addr & 0x3FFF);
            return true;
        }
        return false;
    }

    public override bool MapCpuWrite(ushort addr, out uint mappedAddr, byte data = 0)
    {
        mappedAddr = 0;
        if (addr >= 0x8000 && addr <= 0xFFFF)
        {
            _prgBankSelect = (byte)(data & 0x0F);
            return false;
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

public class Mapper003 : Mapper
{
    private byte _chrBankSelect = 0;

    public Mapper003(byte prgBanks, byte chrBanks) : base(prgBanks, chrBanks) { }

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
            _chrBankSelect = (byte)(data & 0x03);
            return false;
        }
        return false;
    }

    public override bool MapPpuRead(ushort addr, out uint mappedAddr)
    {
        mappedAddr = 0;
        if (addr <= 0x1FFF)
        {
            mappedAddr = (uint)(_chrBankSelect * 0x2000) + addr;
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

public class Mapper004 : Mapper
{
    private byte _targetRegister = 0;
    private bool _prgBankMode = false;
    private bool _chrInversion = false;
    private uint[] _registers = new uint[8];
    private uint[] _prgBankOffsets = new uint[4];
    private uint[] _chrBankOffsets = new uint[8];

    private byte _irqLatch = 0;
    private byte _irqCounter = 0;
    private bool _irqEnable = false;
    private bool _irqReload = false;

    public Mapper004(byte prgBanks, byte chrBanks) : base(prgBanks, chrBanks)
    {
        UpdateOffsets();
    }

    public override MirrorMode MirrorMode => MirrorMode.Hardware;

    public override bool MapCpuRead(ushort addr, out uint mappedAddr)
    {
        mappedAddr = 0;
        if (addr >= 0x6000 && addr <= 0x7FFF)
        {
            mappedAddr = 0x80000000 | (uint)(addr & 0x1FFF);
            return true;
        }
        if (addr >= 0x8000)
        {
            ushort bank = (ushort)((addr - 0x8000) / 0x2000);
            mappedAddr = _prgBankOffsets[bank] + (uint)(addr & 0x1FFF);
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
            bool isEven = (addr & 1) == 0;
            if (addr <= 0x9FFF)
            {
                if (isEven)
                {
                    _targetRegister = (byte)(data & 0x07);
                    _prgBankMode = (data & 0x40) != 0;
                    _chrInversion = (data & 0x80) != 0;
                }
                else
                {
                    _registers[_targetRegister] = data;
                }
                UpdateOffsets();
            }
            else if (addr >= 0xA000 && addr <= 0xBFFF)
            {
                // PRG RAM protect / Mirroring
            }
            else if (addr >= 0xC000 && addr <= 0xDFFF)
            {
                if (isEven) _irqLatch = data;
                else _irqReload = true;
            }
            else if (addr >= 0xE000 && addr <= 0xFFFF)
            {
                if (isEven)
                {
                    _irqEnable = false;
                    irqActive = false;
                }
                else
                {
                    _irqEnable = true;
                }
            }
            return false;
        }
        return false;
    }

    public override bool MapPpuRead(ushort addr, out uint mappedAddr)
    {
        mappedAddr = 0;
        if (addr <= 0x1FFF)
        {
            ushort bank = (ushort)(addr / 0x0400);
            mappedAddr = _chrBankOffsets[bank] + (uint)(addr & 0x03FF);
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

    public override void Scanline()
    {
        if (_irqCounter == 0 || _irqReload)
        {
            _irqCounter = _irqLatch;
            _irqReload = false;
        }
        else
        {
            _irqCounter--;
        }

        if (_irqCounter == 0 && _irqEnable)
        {
            irqActive = true;
        }
    }

    private void UpdateOffsets()
    {
        uint totalPrg = (uint)(prgBanks * 16384);
        uint totalChr = (uint)(Math.Max((byte)1, chrBanks) * 8192);

        if (_prgBankMode)
        {
            _prgBankOffsets[0] = ((uint)(prgBanks * 2 - 2) * 0x2000) % totalPrg;
            _prgBankOffsets[1] = (_registers[7] * 0x2000) % totalPrg;
            _prgBankOffsets[2] = (_registers[6] * 0x2000) % totalPrg;
            _prgBankOffsets[3] = ((uint)(prgBanks * 2 - 1) * 0x2000) % totalPrg;
        }
        else
        {
            _prgBankOffsets[0] = (_registers[6] * 0x2000) % totalPrg;
            _prgBankOffsets[1] = (_registers[7] * 0x2000) % totalPrg;
            _prgBankOffsets[2] = ((uint)(prgBanks * 2 - 2) * 0x2000) % totalPrg;
            _prgBankOffsets[3] = ((uint)(prgBanks * 2 - 1) * 0x2000) % totalPrg;
        }

        if (_chrInversion)
        {
            _chrBankOffsets[0] = (_registers[2] * 0x0400) % totalChr;
            _chrBankOffsets[1] = (_registers[3] * 0x0400) % totalChr;
            _chrBankOffsets[2] = (_registers[4] * 0x0400) % totalChr;
            _chrBankOffsets[3] = (_registers[5] * 0x0400) % totalChr;
            _chrBankOffsets[4] = ((_registers[0] & 0xFE) * 0x0400) % totalChr;
            _chrBankOffsets[5] = ((_registers[0] | 0x01) * 0x0400) % totalChr;
            _chrBankOffsets[6] = ((_registers[1] & 0xFE) * 0x0400) % totalChr;
            _chrBankOffsets[7] = ((_registers[1] | 0x01) * 0x0400) % totalChr;
        }
        else
        {
            _chrBankOffsets[0] = ((_registers[0] & 0xFE) * 0x0400) % totalChr;
            _chrBankOffsets[1] = ((_registers[0] | 0x01) * 0x0400) % totalChr;
            _chrBankOffsets[2] = ((_registers[1] & 0xFE) * 0x0400) % totalChr;
            _chrBankOffsets[3] = ((_registers[1] | 0x01) * 0x0400) % totalChr;
            _chrBankOffsets[4] = (_registers[2] * 0x0400) % totalChr;
            _chrBankOffsets[5] = (_registers[3] * 0x0400) % totalChr;
            _chrBankOffsets[6] = (_registers[4] * 0x0400) % totalChr;
            _chrBankOffsets[7] = (_registers[5] * 0x0400) % totalChr;
        }
    }
}