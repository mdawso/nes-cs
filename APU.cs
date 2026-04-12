using System;

namespace nes;

public class APU
{
    private Bus _bus;

    public APU(Bus bus)
    {
        _bus = bus;
    }

    public void Tick(int cyclesElapsed) {}
}
