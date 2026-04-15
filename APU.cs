using System;

namespace nes;

public class APU
{
    private Bus _bus;
    public bool irqActive = false;

    private int _frameCounterCycles = 0;
    private byte _frameCounterMode = 0;
    private bool _irqInhibit = false;

    public APU(Bus bus)
    {
        _bus = bus;
    }

    public void WriteRegister(ushort addr, byte data)
    {
        if (addr == 0x4017)
        {
            _frameCounterMode = (byte)((data >> 7) & 1);
            _irqInhibit = ((data >> 6) & 1) != 0;

            if (_irqInhibit)
            {
                irqActive = false;
            }

            _frameCounterCycles = 0;
        }
    }

    public byte ReadRegister(ushort addr)
    {
        if (addr == 0x4015)
        {
            byte status = 0;
            
            if (irqActive) status |= 0x40;
            
            irqActive = false;
            return status;
        }
        return 0;
    }

    public void Tick(int cyclesElapsed)
    {
        _frameCounterCycles += cyclesElapsed;

        if (_frameCounterMode == 0)
        {
            if (_frameCounterCycles >= 29830)
            {
                _frameCounterCycles -= 29830;
                if (!_irqInhibit) irqActive = true;
            }
        }
        else
        {
            if (_frameCounterCycles >= 37282)
            {
                _frameCounterCycles -= 37282;
            }
        }
    }
}