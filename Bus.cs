using System;

namespace nes;

public class Bus
{
    public static byte LowByte(ushort word) => (byte)(word & 0xFF);

    public static byte HighByte(ushort word) => (byte)((word >> 8) & 0xFF);

    public static ushort MakeWord(byte low, byte high) => (ushort)((high << 8) | low);

    private byte[] _ram = new byte[2048];

    public CPU cpu;
    public PPU ppu;
    public APU apu;
    public Cartridge? cartridge;

    public byte controllerState;
    private byte _controllerShift;
    private byte _controllerStrobe;

    public Bus()
    {
        cpu = new(this);
        ppu = new(this);
        apu = new(this);
    }

    public void InsertCartridge(Cartridge cart)
    {
        cartridge = cart;
        cpu.Reset();
    }

    public byte ReadByte(ushort addr)
    {
        if (cartridge != null && cartridge.CpuRead(addr, out byte data))
            return data;

        if (addr <= 0x1FFF)
            return _ram[addr & 0x07FF];
        else if (addr >= 0x2000 && addr <= 0x3FFF)
            return ppu.CpuRead((ushort)(addr & 0x0007));
        else if (addr == 0x4016)
        {
            byte result = (byte)((_controllerShift & 1) | 0x40);
            if ((_controllerStrobe & 1) == 0)
                _controllerShift >>= 1;
            return result;
        }

        return 0;
    }

    public void WriteByte(ushort addr, byte val)
    {
        if (cartridge != null && cartridge.CpuWrite(addr, val))
            return;

        if (addr <= 0x1FFF)
            _ram[addr & 0x07FF] = val;
        else if (addr >= 0x2000 && addr <= 0x3FFF)
            ppu.CpuWrite((ushort)(addr & 0x0007), val);
        else if (addr == 0x4014)
        {
            ushort page = (ushort)(val << 8);
            for (int i = 0; i < 256; i++)
            {
                ppu.WriteOAM(ReadByte((ushort)(page + i)));
            }
            cpu.cycles += 513;
            if (cpu.cycles % 2 == 1)
                cpu.cycles++;
        }
        else if (addr == 0x4016)
        {
            _controllerStrobe = (byte)(val & 1);
            if ((_controllerStrobe & 1) != 0)
                _controllerShift = controllerState;
        }
    }

    public ushort ReadWord(ushort addr)
    {
        byte lo = ReadByte(addr);
        byte hi = ReadByte((ushort)(addr + 1));
        return MakeWord(lo, hi);
    }

    public void WriteWord(ushort addr, ushort val)
    {
        WriteByte(addr, LowByte(val));
        WriteByte((ushort)(addr + 1), HighByte(val));
    }

    public void RunFrame()
    {
        while (!ppu.frameComplete)
        {
            int cpuCyclesBefore = cpu.cycles;
            cpu.ExecuteInstruction();
            int cyclesElapsed = cpu.cycles - cpuCyclesBefore;
            
            ppu.CatchUp(cyclesElapsed * 3);
            apu.Tick(cyclesElapsed);
        }
    }
}
