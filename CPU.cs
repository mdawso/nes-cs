using System;

namespace nes;

public class CPU
{
    public byte a, x, y, stack_pointer;
    public ushort program_counter;
    public StatusRegister status = new();
    public int cycles;

    [Flags]
    public enum CpuFlags : byte
    {
        None = 0,
        Carry = 1 << 0,
        Zero = 1 << 1,
        InterruptDisable = 1 << 2,
        Decimal = 1 << 3,
        Break = 1 << 4,
        Unused = 1 << 5,
        Overflow = 1 << 6,
        Negative = 1 << 7
    }

    public class StatusRegister
    {
        private CpuFlags flags;

        public StatusRegister()
        {
            flags = CpuFlags.Unused | CpuFlags.InterruptDisable;
        }

        public byte Value
        {
            get => (byte)flags;
            set => flags = (CpuFlags)value | CpuFlags.Unused;
        }

        public void SetFlag(CpuFlags flag, bool state)
        {
            if (state) flags |= flag;
            else flags &= ~flag;
        }

        public bool HasFlag(CpuFlags flag) => (flags & flag) == flag;

        public void UpdateZeroAndNegative(byte result)
        {
            SetFlag(CpuFlags.Zero, result == 0);
            SetFlag(CpuFlags.Negative, (result & 0x80) != 0);
        }
    }

    private Bus _bus;

    public CPU(Bus bus)
    {
        _bus = bus;
        Reset();
    }

    private byte Read(ushort addr) => _bus.ReadByte(addr);
    private void Write(ushort addr, byte val) => _bus.WriteByte(addr, val);
    private ushort ReadWord(ushort addr) => _bus.ReadWord(addr);

    private void Push(byte val) => Write((ushort)(0x0100 | stack_pointer--), val);
    private byte Pop() => Read((ushort)(0x0100 | ++stack_pointer));

    private void PushWord(ushort val)
    {
        Push((byte)(val >> 8));
        Push((byte)(val & 0xFF));
    }

    private ushort PopWord()
    {
        byte lo = Pop();
        byte hi = Pop();
        return (ushort)(hi << 8 | lo);
    }

    public void Reset()
    {
        a = 0;
        x = 0;
        y = 0;
        stack_pointer = 0xFD;
        status.Value = (byte)(CpuFlags.Unused | CpuFlags.InterruptDisable);
        program_counter = ReadWord(0xFFFC);
        cycles = 8;
    }

    public void NMI()
    {
        PushWord(program_counter);
        status.SetFlag(CpuFlags.Break, false);
        Push((byte)(status.Value | (byte)CpuFlags.Unused));
        status.SetFlag(CpuFlags.InterruptDisable, true);
        program_counter = ReadWord(0xFFFA);
        cycles += 7;
    }

    public void IRQ()
    {
        if (!status.HasFlag(CpuFlags.InterruptDisable))
        {
            PushWord(program_counter);
            status.SetFlag(CpuFlags.Break, false);
            Push((byte)(status.Value | (byte)CpuFlags.Unused));
            status.SetFlag(CpuFlags.InterruptDisable, true);
            program_counter = ReadWord(0xFFFE);
            cycles += 7;
        }
    }

    private bool CrossesPage(ushort a, ushort b) => (a & 0xFF00) != (b & 0xFF00);

    private ushort AddrZeroPage() => Read(program_counter++);
    private ushort AddrZeroPageX() => (ushort)((Read(program_counter++) + x) & 0xFF);
    private ushort AddrZeroPageY() => (ushort)((Read(program_counter++) + y) & 0xFF);
    private ushort AddrAbsolute()
    {
        ushort addr = ReadWord(program_counter);
        program_counter += 2;
        return addr;
    }
    private ushort AddrAbsoluteX(out bool pageCrossed)
    {
        ushort addr = ReadWord(program_counter);
        program_counter += 2;
        ushort result = (ushort)(addr + x);
        pageCrossed = CrossesPage(addr, result);
        return result;
    }
    private ushort AddrAbsoluteY(out bool pageCrossed)
    {
        ushort addr = ReadWord(program_counter);
        program_counter += 2;
        ushort result = (ushort)(addr + y);
        pageCrossed = CrossesPage(addr, result);
        return result;
    }
    private ushort AddrIndirectX()
    {
        byte ptr = (byte)(Read(program_counter++) + x);
        byte lo = Read(ptr);
        byte hi = Read((ushort)((ptr + 1) & 0xFF));
        return (ushort)(hi << 8 | lo);
    }
    private ushort AddrIndirectY(out bool pageCrossed)
    {
        byte ptr = Read(program_counter++);
        byte lo = Read(ptr);
        byte hi = Read((ushort)((ptr + 1) & 0xFF));
        ushort addr = (ushort)(hi << 8 | lo);
        ushort result = (ushort)(addr + y);
        pageCrossed = CrossesPage(addr, result);
        return result;
    }

    private void Branch(bool condition)
    {
        sbyte offset = (sbyte)Read(program_counter++);
        if (condition)
        {
            cycles++;
            ushort newPc = (ushort)(program_counter + offset);
            if (CrossesPage(program_counter, newPc)) cycles++;
            program_counter = newPc;
        }
    }

    private void ADC(byte val)
    {
        int sum = a + val + (status.HasFlag(CpuFlags.Carry) ? 1 : 0);
        status.SetFlag(CpuFlags.Carry, sum > 0xFF);
        status.SetFlag(CpuFlags.Overflow, (~(a ^ val) & (a ^ sum) & 0x80) != 0);
        a = (byte)sum;
        status.UpdateZeroAndNegative(a);
    }

    private void SBC(byte val)
    {
        ADC((byte)~val);
    }

    private void CMP(byte reg, byte val)
    {
        int diff = reg - val;
        status.SetFlag(CpuFlags.Carry, reg >= val);
        status.UpdateZeroAndNegative((byte)diff);
    }

    private void AND(byte val)
    {
        a &= val;
        status.UpdateZeroAndNegative(a);
    }

    private void ORA(byte val)
    {
        a |= val;
        status.UpdateZeroAndNegative(a);
    }

    private void EOR(byte val)
    {
        a ^= val;
        status.UpdateZeroAndNegative(a);
    }

    private byte ASL(byte val)
    {
        status.SetFlag(CpuFlags.Carry, (val & 0x80) != 0);
        val <<= 1;
        status.UpdateZeroAndNegative(val);
        return val;
    }

    private byte LSR(byte val)
    {
        status.SetFlag(CpuFlags.Carry, (val & 0x01) != 0);
        val >>= 1;
        status.UpdateZeroAndNegative(val);
        return val;
    }

    private byte ROL(byte val)
    {
        bool oldCarry = status.HasFlag(CpuFlags.Carry);
        status.SetFlag(CpuFlags.Carry, (val & 0x80) != 0);
        val = (byte)((val << 1) | (oldCarry ? 1 : 0));
        status.UpdateZeroAndNegative(val);
        return val;
    }

    private byte ROR(byte val)
    {
        bool oldCarry = status.HasFlag(CpuFlags.Carry);
        status.SetFlag(CpuFlags.Carry, (val & 0x01) != 0);
        val = (byte)((val >> 1) | (oldCarry ? 0x80 : 0));
        status.UpdateZeroAndNegative(val);
        return val;
    }

    private byte INC(byte val)
    {
        val++;
        status.UpdateZeroAndNegative(val);
        return val;
    }

    private byte DEC(byte val)
    {
        val--;
        status.UpdateZeroAndNegative(val);
        return val;
    }

    private void BIT(byte val)
    {
        status.SetFlag(CpuFlags.Zero, (a & val) == 0);
        status.SetFlag(CpuFlags.Negative, (val & 0x80) != 0);
        status.SetFlag(CpuFlags.Overflow, (val & 0x40) != 0);
    }

    public void ExecuteInstruction()
    {
        byte opcode = Read(program_counter++);
        bool pageCrossed = false;
        ushort addr = 0;
        byte val = 0;
        byte res = 0;

        switch (opcode)
        {
            case 0x69: val = Read(program_counter++); ADC(val); cycles += 2; break;
            case 0x65: addr = AddrZeroPage(); ADC(Read(addr)); cycles += 3; break;
            case 0x75: addr = AddrZeroPageX(); ADC(Read(addr)); cycles += 4; break;
            case 0x6D: addr = AddrAbsolute(); ADC(Read(addr)); cycles += 4; break;
            case 0x7D: addr = AddrAbsoluteX(out pageCrossed); ADC(Read(addr)); cycles += 4 + (pageCrossed ? 1 : 0); break;
            case 0x79: addr = AddrAbsoluteY(out pageCrossed); ADC(Read(addr)); cycles += 4 + (pageCrossed ? 1 : 0); break;
            case 0x61: addr = AddrIndirectX(); ADC(Read(addr)); cycles += 6; break;
            case 0x71: addr = AddrIndirectY(out pageCrossed); ADC(Read(addr)); cycles += 5 + (pageCrossed ? 1 : 0); break;

            case 0xEB: 
            case 0xE9: val = Read(program_counter++); SBC(val); cycles += 2; break;
            case 0xE5: addr = AddrZeroPage(); SBC(Read(addr)); cycles += 3; break;
            case 0xF5: addr = AddrZeroPageX(); SBC(Read(addr)); cycles += 4; break;
            case 0xED: addr = AddrAbsolute(); SBC(Read(addr)); cycles += 4; break;
            case 0xFD: addr = AddrAbsoluteX(out pageCrossed); SBC(Read(addr)); cycles += 4 + (pageCrossed ? 1 : 0); break;
            case 0xF9: addr = AddrAbsoluteY(out pageCrossed); SBC(Read(addr)); cycles += 4 + (pageCrossed ? 1 : 0); break;
            case 0xE1: addr = AddrIndirectX(); SBC(Read(addr)); cycles += 6; break;
            case 0xF1: addr = AddrIndirectY(out pageCrossed); SBC(Read(addr)); cycles += 5 + (pageCrossed ? 1 : 0); break;

            case 0x29: val = Read(program_counter++); AND(val); cycles += 2; break;
            case 0x25: addr = AddrZeroPage(); AND(Read(addr)); cycles += 3; break;
            case 0x35: addr = AddrZeroPageX(); AND(Read(addr)); cycles += 4; break;
            case 0x2D: addr = AddrAbsolute(); AND(Read(addr)); cycles += 4; break;
            case 0x3D: addr = AddrAbsoluteX(out pageCrossed); AND(Read(addr)); cycles += 4 + (pageCrossed ? 1 : 0); break;
            case 0x39: addr = AddrAbsoluteY(out pageCrossed); AND(Read(addr)); cycles += 4 + (pageCrossed ? 1 : 0); break;
            case 0x21: addr = AddrIndirectX(); AND(Read(addr)); cycles += 6; break;
            case 0x31: addr = AddrIndirectY(out pageCrossed); AND(Read(addr)); cycles += 5 + (pageCrossed ? 1 : 0); break;

            case 0x09: val = Read(program_counter++); ORA(val); cycles += 2; break;
            case 0x05: addr = AddrZeroPage(); ORA(Read(addr)); cycles += 3; break;
            case 0x15: addr = AddrZeroPageX(); ORA(Read(addr)); cycles += 4; break;
            case 0x0D: addr = AddrAbsolute(); ORA(Read(addr)); cycles += 4; break;
            case 0x1D: addr = AddrAbsoluteX(out pageCrossed); ORA(Read(addr)); cycles += 4 + (pageCrossed ? 1 : 0); break;
            case 0x19: addr = AddrAbsoluteY(out pageCrossed); ORA(Read(addr)); cycles += 4 + (pageCrossed ? 1 : 0); break;
            case 0x01: addr = AddrIndirectX(); ORA(Read(addr)); cycles += 6; break;
            case 0x11: addr = AddrIndirectY(out pageCrossed); ORA(Read(addr)); cycles += 5 + (pageCrossed ? 1 : 0); break;

            case 0x49: val = Read(program_counter++); EOR(val); cycles += 2; break;
            case 0x45: addr = AddrZeroPage(); EOR(Read(addr)); cycles += 3; break;
            case 0x55: addr = AddrZeroPageX(); EOR(Read(addr)); cycles += 4; break;
            case 0x4D: addr = AddrAbsolute(); EOR(Read(addr)); cycles += 4; break;
            case 0x5D: addr = AddrAbsoluteX(out pageCrossed); EOR(Read(addr)); cycles += 4 + (pageCrossed ? 1 : 0); break;
            case 0x59: addr = AddrAbsoluteY(out pageCrossed); EOR(Read(addr)); cycles += 4 + (pageCrossed ? 1 : 0); break;
            case 0x41: addr = AddrIndirectX(); EOR(Read(addr)); cycles += 6; break;
            case 0x51: addr = AddrIndirectY(out pageCrossed); EOR(Read(addr)); cycles += 5 + (pageCrossed ? 1 : 0); break;

            case 0x0A: a = ASL(a); cycles += 2; break;
            case 0x06: addr = AddrZeroPage(); Write(addr, ASL(Read(addr))); cycles += 5; break;
            case 0x16: addr = AddrZeroPageX(); Write(addr, ASL(Read(addr))); cycles += 6; break;
            case 0x0E: addr = AddrAbsolute(); Write(addr, ASL(Read(addr))); cycles += 6; break;
            case 0x1E: addr = AddrAbsoluteX(out _); Write(addr, ASL(Read(addr))); cycles += 7; break;

            case 0x4A: a = LSR(a); cycles += 2; break;
            case 0x46: addr = AddrZeroPage(); Write(addr, LSR(Read(addr))); cycles += 5; break;
            case 0x56: addr = AddrZeroPageX(); Write(addr, LSR(Read(addr))); cycles += 6; break;
            case 0x4E: addr = AddrAbsolute(); Write(addr, LSR(Read(addr))); cycles += 6; break;
            case 0x5E: addr = AddrAbsoluteX(out _); Write(addr, LSR(Read(addr))); cycles += 7; break;

            case 0x2A: a = ROL(a); cycles += 2; break;
            case 0x26: addr = AddrZeroPage(); Write(addr, ROL(Read(addr))); cycles += 5; break;
            case 0x36: addr = AddrZeroPageX(); Write(addr, ROL(Read(addr))); cycles += 6; break;
            case 0x2E: addr = AddrAbsolute(); Write(addr, ROL(Read(addr))); cycles += 6; break;
            case 0x3E: addr = AddrAbsoluteX(out _); Write(addr, ROL(Read(addr))); cycles += 7; break;

            case 0x6A: a = ROR(a); cycles += 2; break;
            case 0x66: addr = AddrZeroPage(); Write(addr, ROR(Read(addr))); cycles += 5; break;
            case 0x76: addr = AddrZeroPageX(); Write(addr, ROR(Read(addr))); cycles += 6; break;
            case 0x6E: addr = AddrAbsolute(); Write(addr, ROR(Read(addr))); cycles += 6; break;
            case 0x7E: addr = AddrAbsoluteX(out _); Write(addr, ROR(Read(addr))); cycles += 7; break;

            case 0xE6: addr = AddrZeroPage(); Write(addr, INC(Read(addr))); cycles += 5; break;
            case 0xF6: addr = AddrZeroPageX(); Write(addr, INC(Read(addr))); cycles += 6; break;
            case 0xEE: addr = AddrAbsolute(); Write(addr, INC(Read(addr))); cycles += 6; break;
            case 0xFE: addr = AddrAbsoluteX(out _); Write(addr, INC(Read(addr))); cycles += 7; break;

            case 0xC6: addr = AddrZeroPage(); Write(addr, DEC(Read(addr))); cycles += 5; break;
            case 0xD6: addr = AddrZeroPageX(); Write(addr, DEC(Read(addr))); cycles += 6; break;
            case 0xCE: addr = AddrAbsolute(); Write(addr, DEC(Read(addr))); cycles += 6; break;
            case 0xDE: addr = AddrAbsoluteX(out _); Write(addr, DEC(Read(addr))); cycles += 7; break;

            case 0xC9: val = Read(program_counter++); CMP(a, val); cycles += 2; break;
            case 0xC5: addr = AddrZeroPage(); CMP(a, Read(addr)); cycles += 3; break;
            case 0xD5: addr = AddrZeroPageX(); CMP(a, Read(addr)); cycles += 4; break;
            case 0xCD: addr = AddrAbsolute(); CMP(a, Read(addr)); cycles += 4; break;
            case 0xDD: addr = AddrAbsoluteX(out pageCrossed); CMP(a, Read(addr)); cycles += 4 + (pageCrossed ? 1 : 0); break;
            case 0xD9: addr = AddrAbsoluteY(out pageCrossed); CMP(a, Read(addr)); cycles += 4 + (pageCrossed ? 1 : 0); break;
            case 0xC1: addr = AddrIndirectX(); CMP(a, Read(addr)); cycles += 6; break;
            case 0xD1: addr = AddrIndirectY(out pageCrossed); CMP(a, Read(addr)); cycles += 5 + (pageCrossed ? 1 : 0); break;

            case 0xE0: val = Read(program_counter++); CMP(x, val); cycles += 2; break;
            case 0xE4: addr = AddrZeroPage(); CMP(x, Read(addr)); cycles += 3; break;
            case 0xEC: addr = AddrAbsolute(); CMP(x, Read(addr)); cycles += 4; break;

            case 0xC0: val = Read(program_counter++); CMP(y, val); cycles += 2; break;
            case 0xC4: addr = AddrZeroPage(); CMP(y, Read(addr)); cycles += 3; break;
            case 0xCC: addr = AddrAbsolute(); CMP(y, Read(addr)); cycles += 4; break;

            case 0x24: addr = AddrZeroPage(); BIT(Read(addr)); cycles += 3; break;
            case 0x2C: addr = AddrAbsolute(); BIT(Read(addr)); cycles += 4; break;

            case 0xA9: a = Read(program_counter++); status.UpdateZeroAndNegative(a); cycles += 2; break;
            case 0xA5: addr = AddrZeroPage(); a = Read(addr); status.UpdateZeroAndNegative(a); cycles += 3; break;
            case 0xB5: addr = AddrZeroPageX(); a = Read(addr); status.UpdateZeroAndNegative(a); cycles += 4; break;
            case 0xAD: addr = AddrAbsolute(); a = Read(addr); status.UpdateZeroAndNegative(a); cycles += 4; break;
            case 0xBD: addr = AddrAbsoluteX(out pageCrossed); a = Read(addr); status.UpdateZeroAndNegative(a); cycles += 4 + (pageCrossed ? 1 : 0); break;
            case 0xB9: addr = AddrAbsoluteY(out pageCrossed); a = Read(addr); status.UpdateZeroAndNegative(a); cycles += 4 + (pageCrossed ? 1 : 0); break;
            case 0xA1: addr = AddrIndirectX(); a = Read(addr); status.UpdateZeroAndNegative(a); cycles += 6; break;
            case 0xB1: addr = AddrIndirectY(out pageCrossed); a = Read(addr); status.UpdateZeroAndNegative(a); cycles += 5 + (pageCrossed ? 1 : 0); break;

            case 0xA2: x = Read(program_counter++); status.UpdateZeroAndNegative(x); cycles += 2; break;
            case 0xA6: addr = AddrZeroPage(); x = Read(addr); status.UpdateZeroAndNegative(x); cycles += 3; break;
            case 0xB6: addr = AddrZeroPageY(); x = Read(addr); status.UpdateZeroAndNegative(x); cycles += 4; break;
            case 0xAE: addr = AddrAbsolute(); x = Read(addr); status.UpdateZeroAndNegative(x); cycles += 4; break;
            case 0xBE: addr = AddrAbsoluteY(out pageCrossed); x = Read(addr); status.UpdateZeroAndNegative(x); cycles += 4 + (pageCrossed ? 1 : 0); break;

            case 0xA0: y = Read(program_counter++); status.UpdateZeroAndNegative(y); cycles += 2; break;
            case 0xA4: addr = AddrZeroPage(); y = Read(addr); status.UpdateZeroAndNegative(y); cycles += 3; break;
            case 0xB4: addr = AddrZeroPageX(); y = Read(addr); status.UpdateZeroAndNegative(y); cycles += 4; break;
            case 0xAC: addr = AddrAbsolute(); y = Read(addr); status.UpdateZeroAndNegative(y); cycles += 4; break;
            case 0xBC: addr = AddrAbsoluteX(out pageCrossed); y = Read(addr); status.UpdateZeroAndNegative(y); cycles += 4 + (pageCrossed ? 1 : 0); break;

            case 0x85: addr = AddrZeroPage(); Write(addr, a); cycles += 3; break;
            case 0x95: addr = AddrZeroPageX(); Write(addr, a); cycles += 4; break;
            case 0x8D: addr = AddrAbsolute(); Write(addr, a); cycles += 4; break;
            case 0x9D: addr = AddrAbsoluteX(out _); Write(addr, a); cycles += 5; break;
            case 0x99: addr = AddrAbsoluteY(out _); Write(addr, a); cycles += 5; break;
            case 0x81: addr = AddrIndirectX(); Write(addr, a); cycles += 6; break;
            case 0x91: addr = AddrIndirectY(out _); Write(addr, a); cycles += 6; break;

            case 0x86: addr = AddrZeroPage(); Write(addr, x); cycles += 3; break;
            case 0x96: addr = AddrZeroPageY(); Write(addr, x); cycles += 4; break;
            case 0x8E: addr = AddrAbsolute(); Write(addr, x); cycles += 4; break;

            case 0x84: addr = AddrZeroPage(); Write(addr, y); cycles += 3; break;
            case 0x94: addr = AddrZeroPageX(); Write(addr, y); cycles += 4; break;
            case 0x8C: addr = AddrAbsolute(); Write(addr, y); cycles += 4; break;

            case 0xAA: x = a; status.UpdateZeroAndNegative(x); cycles += 2; break;
            case 0xA8: y = a; status.UpdateZeroAndNegative(y); cycles += 2; break;
            case 0xBA: x = stack_pointer; status.UpdateZeroAndNegative(x); cycles += 2; break;
            case 0x8A: a = x; status.UpdateZeroAndNegative(a); cycles += 2; break;
            case 0x9A: stack_pointer = x; cycles += 2; break;
            case 0x98: a = y; status.UpdateZeroAndNegative(a); cycles += 2; break;

            case 0xE8: x++; status.UpdateZeroAndNegative(x); cycles += 2; break;
            case 0xC8: y++; status.UpdateZeroAndNegative(y); cycles += 2; break;
            case 0xCA: x--; status.UpdateZeroAndNegative(x); cycles += 2; break;
            case 0x88: y--; status.UpdateZeroAndNegative(y); cycles += 2; break;

            case 0x4C: program_counter = AddrAbsolute(); cycles += 3; break;
            case 0x6C:
                ushort jumpAddr = ReadWord(program_counter);
                ushort jumpAddrHi = (ushort)((jumpAddr & 0xFF00) | ((jumpAddr + 1) & 0x00FF));
                program_counter = (ushort)(Read(jumpAddrHi) << 8 | Read(jumpAddr));
                cycles += 5;
                break;

            case 0x20: PushWord((ushort)(program_counter + 1)); program_counter = AddrAbsolute(); cycles += 6; break;
            case 0x60: program_counter = (ushort)(PopWord() + 1); cycles += 6; break;

            case 0x90: Branch(!status.HasFlag(CpuFlags.Carry)); cycles += 2; break;
            case 0xB0: Branch(status.HasFlag(CpuFlags.Carry)); cycles += 2; break;
            case 0xF0: Branch(status.HasFlag(CpuFlags.Zero)); cycles += 2; break;
            case 0xD0: Branch(!status.HasFlag(CpuFlags.Zero)); cycles += 2; break;
            case 0x10: Branch(!status.HasFlag(CpuFlags.Negative)); cycles += 2; break;
            case 0x30: Branch(status.HasFlag(CpuFlags.Negative)); cycles += 2; break;
            case 0x50: Branch(!status.HasFlag(CpuFlags.Overflow)); cycles += 2; break;
            case 0x70: Branch(status.HasFlag(CpuFlags.Overflow)); cycles += 2; break;

            case 0x18: status.SetFlag(CpuFlags.Carry, false); cycles += 2; break;
            case 0x38: status.SetFlag(CpuFlags.Carry, true); cycles += 2; break;
            case 0x58: status.SetFlag(CpuFlags.InterruptDisable, false); cycles += 2; break;
            case 0x78: status.SetFlag(CpuFlags.InterruptDisable, true); cycles += 2; break;
            case 0xB8: status.SetFlag(CpuFlags.Overflow, false); cycles += 2; break;
            case 0xD8: status.SetFlag(CpuFlags.Decimal, false); cycles += 2; break;
            case 0xF8: status.SetFlag(CpuFlags.Decimal, true); cycles += 2; break;

            case 0x00:
                program_counter++;
                PushWord(program_counter);
                status.SetFlag(CpuFlags.Break, false);
                Push((byte)(status.Value | (byte)CpuFlags.Unused));
                status.SetFlag(CpuFlags.InterruptDisable, true);
                program_counter = ReadWord(0xFFFE);
                cycles += 7;
                break;

            case 0x40:
                status.Value = Pop();
                status.SetFlag(CpuFlags.Unused, true);
                program_counter = PopWord();
                cycles += 6;
                break;

            case 0x48: Push(a); cycles += 3; break;
            case 0x68: a = Pop(); status.UpdateZeroAndNegative(a); cycles += 4; break;
            case 0x08: Push((byte)(status.Value | (byte)CpuFlags.Break | (byte)CpuFlags.Unused)); cycles += 3; break;
            case 0x28: status.Value = Pop(); status.SetFlag(CpuFlags.Unused, true); cycles += 4; break;

            case 0xEA: cycles += 2; break;

            case 0xA7: 
            case 0xB7: 
            case 0xA3: 
            case 0xB3: 
            case 0xAF: 
            case 0xBF: 
                if (opcode == 0xA7) { addr = AddrZeroPage(); cycles += 3; }
                else if (opcode == 0xB7) { addr = AddrZeroPageY(); cycles += 4; }
                else if (opcode == 0xA3) { addr = AddrIndirectX(); cycles += 6; }
                else if (opcode == 0xB3) { addr = AddrIndirectY(out pageCrossed); cycles += 5 + (pageCrossed ? 1 : 0); }
                else if (opcode == 0xAF) { addr = AddrAbsolute(); cycles += 4; }
                else { addr = AddrAbsoluteY(out pageCrossed); cycles += 4 + (pageCrossed ? 1 : 0); }
                val = Read(addr);
                a = val;
                x = val;
                status.UpdateZeroAndNegative(a);
                break;

            case 0x87: 
            case 0x97: 
            case 0x83: 
            case 0x93: 
            case 0x8F: 
                if (opcode == 0x87) { addr = AddrZeroPage(); cycles += 3; }
                else if (opcode == 0x97) { addr = AddrZeroPageY(); cycles += 4; }
                else if (opcode == 0x83) { addr = AddrIndirectX(); cycles += 6; }
                else if (opcode == 0x93) { addr = AddrIndirectY(out _); cycles += 6; }
                else { addr = AddrAbsolute(); cycles += 4; }
                Write(addr, (byte)(a & x));
                break;

            case 0xC7: 
            case 0xD7: 
            case 0xC3: 
            case 0xD3: 
            case 0xCF: 
            case 0xDF: 
                if (opcode == 0xC7) { addr = AddrZeroPage(); cycles += 5; }
                else if (opcode == 0xD7) { addr = AddrZeroPageX(); cycles += 6; }
                else if (opcode == 0xC3) { addr = AddrIndirectX(); cycles += 8; }
                else if (opcode == 0xD3) { addr = AddrIndirectY(out _); cycles += 8; }
                else if (opcode == 0xCF) { addr = AddrAbsolute(); cycles += 6; }
                else { addr = AddrAbsoluteX(out _); cycles += 7; }
                res = DEC(Read(addr));
                Write(addr, res);
                CMP(a, res);
                break;

            case 0xE7: 
            case 0xF7: 
            case 0xE3: 
            case 0xF3: 
            case 0xEF: 
            case 0xFF: 
                if (opcode == 0xE7) { addr = AddrZeroPage(); cycles += 5; }
                else if (opcode == 0xF7) { addr = AddrZeroPageX(); cycles += 6; }
                else if (opcode == 0xE3) { addr = AddrIndirectX(); cycles += 8; }
                else if (opcode == 0xF3) { addr = AddrIndirectY(out _); cycles += 8; }
                else if (opcode == 0xEF) { addr = AddrAbsolute(); cycles += 6; }
                else { addr = AddrAbsoluteX(out _); cycles += 7; }
                res = INC(Read(addr));
                Write(addr, res);
                SBC(res);
                break;

            case 0x07: 
            case 0x17: 
            case 0x03: 
            case 0x13: 
            case 0x0F: 
            case 0x1F: 
                if (opcode == 0x07) { addr = AddrZeroPage(); cycles += 5; }
                else if (opcode == 0x17) { addr = AddrZeroPageX(); cycles += 6; }
                else if (opcode == 0x03) { addr = AddrIndirectX(); cycles += 8; }
                else if (opcode == 0x13) { addr = AddrIndirectY(out _); cycles += 8; }
                else if (opcode == 0x0F) { addr = AddrAbsolute(); cycles += 6; }
                else { addr = AddrAbsoluteX(out _); cycles += 7; }
                res = ASL(Read(addr));
                Write(addr, res);
                ORA(res);
                break;

            case 0x27: 
            case 0x37: 
            case 0x23: 
            case 0x33: 
            case 0x2F: 
            case 0x3F: 
                if (opcode == 0x27) { addr = AddrZeroPage(); cycles += 5; }
                else if (opcode == 0x37) { addr = AddrZeroPageX(); cycles += 6; }
                else if (opcode == 0x23) { addr = AddrIndirectX(); cycles += 8; }
                else if (opcode == 0x33) { addr = AddrIndirectY(out _); cycles += 8; }
                else if (opcode == 0x2F) { addr = AddrAbsolute(); cycles += 6; }
                else { addr = AddrAbsoluteX(out _); cycles += 7; }
                res = ROL(Read(addr));
                Write(addr, res);
                AND(res);
                break;

            case 0x47: 
            case 0x57: 
            case 0x43: 
            case 0x53: 
            case 0x4F: 
            case 0x5F: 
                if (opcode == 0x47) { addr = AddrZeroPage(); cycles += 5; }
                else if (opcode == 0x57) { addr = AddrZeroPageX(); cycles += 6; }
                else if (opcode == 0x43) { addr = AddrIndirectX(); cycles += 8; }
                else if (opcode == 0x53) { addr = AddrIndirectY(out _); cycles += 8; }
                else if (opcode == 0x4F) { addr = AddrAbsolute(); cycles += 6; }
                else { addr = AddrAbsoluteX(out _); cycles += 7; }
                res = LSR(Read(addr));
                Write(addr, res);
                EOR(res);
                break;

            case 0x67: 
            case 0x77: 
            case 0x63: 
            case 0x73: 
            case 0x6F: 
            case 0x7F: 
                if (opcode == 0x67) { addr = AddrZeroPage(); cycles += 5; }
                else if (opcode == 0x77) { addr = AddrZeroPageX(); cycles += 6; }
                else if (opcode == 0x63) { addr = AddrIndirectX(); cycles += 8; }
                else if (opcode == 0x73) { addr = AddrIndirectY(out _); cycles += 8; }
                else if (opcode == 0x6F) { addr = AddrAbsolute(); cycles += 6; }
                else { addr = AddrAbsoluteX(out _); cycles += 7; }
                res = ROR(Read(addr));
                Write(addr, res);
                ADC(res);
                break;

            case 0x1A: 
            case 0x3A: 
            case 0x5A: 
            case 0x7A: 
            case 0xDA: 
            case 0xFA: 
                cycles += 2;
                break;

            case 0x04: case 0x44: case 0x64:
                addr = AddrZeroPage(); cycles += 3; break;
            case 0x14: case 0x34: case 0x54: case 0x74: case 0xD4: case 0xF4:
                addr = AddrZeroPageX(); cycles += 4; break;
            case 0x0C:
                addr = AddrAbsolute(); cycles += 4; break;
            case 0x1C: case 0x3C: case 0x5C: case 0x7C: case 0xDC: case 0xFC:
                addr = AddrAbsoluteX(out pageCrossed); cycles += 4 + (pageCrossed ? 1 : 0); break;

            default:
                Console.WriteLine($"Invalid Opcode: {opcode}");
                cycles += 2;
                break;
        }
    }
}

