using System;

namespace nes;

public class PPU
{
    private Bus _bus;
    public uint[] screenBuffer = new uint[256 * 240];
    public bool frameComplete = false;

    private byte[,] _nameTables = new byte[2, 1024];
    private byte[] _paletteTable = new byte[32];
    private byte[] _oam = new byte[256];

    public int scanline = 0;
    public int cycle = 0;

    private byte _status = 0;
    private byte _ctrl = 0;
    private byte _mask = 0;

    private ushort _vramAddr = 0;
    private ushort _tempVramAddr = 0;
    private byte _fineX = 0;
    private bool _addressLatch = false;
    private byte _dataBuffer = 0;
    private byte _oamAddr = 0;

    private ushort _bgShifterPatternLo;
    private ushort _bgShifterPatternHi;
    private ushort _bgShifterPaletteLo;
    private ushort _bgShifterPaletteHi;
    
    private byte _nextTileId;
    private byte _nextPalette;
    private byte _nextPatternLo;
    private byte _nextPatternHi;

    private static readonly uint[] SystemColourPalette = {
        0xFF545454, 0xFF001E74, 0xFF081090, 0xFF300088, 0xFF440064, 0xFF5C0030, 0xFF540400, 0xFF3C1800, 
        0xFF202A00, 0xFF083A00, 0xFF004000, 0xFF003C00, 0xFF00323C, 0xFF000000, 0xFF000000, 0xFF000000,
        0xFF989698, 0xFF084CC4, 0xFF3032EC, 0xFF5C1EE4, 0xFF8814B0, 0xFFA01464, 0xFF982220, 0xFF783C00, 
        0xFF545A00, 0xFF287200, 0xFF087C00, 0xFF007628, 0xFF006678, 0xFF000000, 0xFF000000, 0xFF000000,
        0xFFECEEEC, 0xFF4C9AEC, 0xFF787DEC, 0xFFB062EC, 0xFFE454EC, 0xFFEC58B4, 0xFFEC6A64, 0xFFD48820, 
        0xFFA0AA00, 0xFF74C400, 0xFF4CD020, 0xFF38CC6C, 0xFF38B4CC, 0xFF3C3C3C, 0xFF000000, 0xFF000000,
        0xFFECEEEC, 0xFFA8CCEC, 0xFFBCBCEC, 0xFFD4B2EC, 0xFFECAEEC, 0xFFECAED4, 0xFFECB4B0, 0xFFE4C490, 
        0xFFCCD278, 0xFFB4DE78, 0xFFA8E290, 0xFF98E2B4, 0xFFA0D6E4, 0xFFA0A2A0, 0xFF000000, 0xFF000000
    };

    private static uint ToRgba(uint argb)
    {
        uint a = (argb >> 24) & 0xFF;
        uint r = (argb >> 16) & 0xFF;
        uint g = (argb >> 8) & 0xFF;
        uint b = argb & 0xFF;
        return (a << 24) | (b << 16) | (g << 8) | r;
    }

    private int MapNametableIndex(ushort addr)
    {
        int table = (addr >> 10) & 0x03;
        
        MirrorMode mode = _bus.cartridge?.Mapper?.MirrorMode ?? MirrorMode.Hardware;

        if (mode == MirrorMode.Hardware)
        {
            bool vertical = _bus.cartridge?.VerticalMirroring ?? true;
            mode = vertical ? MirrorMode.Vertical : MirrorMode.Horizontal;
        }
        
        return mode switch
        {
            MirrorMode.Vertical => table & 1,
            MirrorMode.Horizontal => table >> 1,
            MirrorMode.SingleScreenLower => 0,
            MirrorMode.SingleScreenUpper => 1,
            _ => table & 1
        };
    }

    private bool TryGetSpritePixel(int x, int y, out byte palette, out bool priority, out byte pixel, out bool isSpriteZero)
    {
        int height = (_ctrl & 0x20) != 0 ? 16 : 8;

        for (int i = 0; i < 64; i++)
        {
            int baseIndex = i * 4;
            int spriteY = _oam[baseIndex];
            int spriteX = _oam[baseIndex + 3];
            int top = spriteY + 1;

            if (y < top || y >= top + height) continue;
            if (x < spriteX || x >= spriteX + 8) continue;

            byte tileIndex = _oam[baseIndex + 1];
            byte attr = _oam[baseIndex + 2];

            bool flipH = (attr & 0x40) != 0;
            bool flipV = (attr & 0x80) != 0;

            int spriteRow = y - top;
            if (flipV) spriteRow = height - 1 - spriteRow;

            ushort patternBase;
            int tile = tileIndex;

            if (height == 16)
            {
                patternBase = (ushort)((tileIndex & 1) != 0 ? 0x1000 : 0x0000);
                tile = tileIndex & 0xFE;
                if (spriteRow >= 8)
                {
                    tile += 1;
                    spriteRow -= 8;
                }
            }
            else
            {
                patternBase = (ushort)((_ctrl & 0x08) != 0 ? 0x1000 : 0x0000);
            }

            int fineX = x - spriteX;
            if (flipH) fineX = 7 - fineX;

            ushort patternAddr = (ushort)(patternBase + tile * 16 + spriteRow);
            byte patternLo = PpuRead(patternAddr);
            byte patternHi = PpuRead((ushort)(patternAddr + 8));

            int bit = 7 - fineX;
            byte p = (byte)(((patternHi >> bit) & 1) << 1 | ((patternLo >> bit) & 1));

            if (p == 0) continue;

            palette = (byte)(attr & 0x03);
            priority = (attr & 0x20) != 0;
            pixel = p;
            isSpriteZero = (i == 0);
            return true;
        }

        palette = 0;
        priority = false;
        pixel = 0;
        isSpriteZero = false;
        return false;
    }

    public PPU(Bus bus)
    {
        _bus = bus;
    }

    public byte CpuRead(ushort addr)
    {
        byte data = 0;
        switch (addr)
        {
            case 0x0002:
                data = (byte)((_status & 0xE0) | (_dataBuffer & 0x1F));
                _status &= 0x7F;
                _addressLatch = false;
                break;
            case 0x0004:
                data = _oam[_oamAddr];
                break;
            case 0x0007:
                data = _dataBuffer;
                _dataBuffer = PpuRead(_vramAddr);
                if (_vramAddr >= 0x3F00) data = _dataBuffer;
                _vramAddr += (ushort)((_ctrl & 0x04) != 0 ? 32 : 1);
                break;
        }
        return data;
    }

    public void CpuWrite(ushort addr, byte data)
    {
        switch (addr)
        {
            case 0x0000:
                _ctrl = data;
                _tempVramAddr = (ushort)((_tempVramAddr & 0xF3FF) | ((data & 0x03) << 10));
                break;
            case 0x0001:
                _mask = data;
                break;
            case 0x0003:
                _oamAddr = data;
                break;
            case 0x0004:
                _oam[_oamAddr++] = data;
                break;
            case 0x0005:
                if (!_addressLatch)
                {
                    _fineX = (byte)(data & 0x07);
                    _tempVramAddr = (ushort)((_tempVramAddr & 0xFFE0) | (data >> 3));
                    _addressLatch = true;
                }
                else
                {
                    _tempVramAddr = (ushort)((_tempVramAddr & 0x8FFF) | ((data & 0x07) << 12));
                    _tempVramAddr = (ushort)((_tempVramAddr & 0xFC1F) | ((data & 0xF8) << 2));
                    _addressLatch = false;
                }
                break;
            case 0x0006:
                if (!_addressLatch)
                {
                    _tempVramAddr = (ushort)((_tempVramAddr & 0x00FF) | ((data & 0x3F) << 8));
                    _addressLatch = true;
                }
                else
                {
                    _tempVramAddr = (ushort)((_tempVramAddr & 0xFF00) | data);
                    _vramAddr = _tempVramAddr;
                    _addressLatch = false;
                }
                break;
            case 0x0007:
                PpuWrite(_vramAddr, data);
                _vramAddr += (ushort)((_ctrl & 0x04) != 0 ? 32 : 1);
                break;
        }
    }

    public byte PpuRead(ushort addr)
    {
        addr &= 0x3FFF;
        if (_bus.cartridge != null && _bus.cartridge.PpuRead(addr, out byte cartData))
            return cartData;

        if (addr >= 0x2000 && addr <= 0x3EFF)
        {
            addr &= 0x0FFF;
            int table = MapNametableIndex(addr);
            int offset = addr & 0x03FF;
            return _nameTables[table, offset];
        }
        else if (addr >= 0x3F00 && addr <= 0x3FFF)
        {
            addr &= 0x001F;
            if (addr == 0x0010 || addr == 0x0014 || addr == 0x0018 || addr == 0x001C)
                addr -= 0x0010;
            return _paletteTable[addr];
        }
        return 0;
    }

    public void PpuWrite(ushort addr, byte data)
    {
        addr &= 0x3FFF;
        if (_bus.cartridge != null && _bus.cartridge.PpuWrite(addr, data))
            return;

        if (addr >= 0x2000 && addr <= 0x3EFF)
        {
            addr &= 0x0FFF;
            int table = MapNametableIndex(addr);
            int offset = addr & 0x03FF;
            _nameTables[table, offset] = data;
        }
        else if (addr >= 0x3F00 && addr <= 0x3FFF)
        {
            addr &= 0x001F;
            if (addr == 0x0010 || addr == 0x0014 || addr == 0x0018 || addr == 0x001C)
                addr -= 0x0010;
            _paletteTable[addr] = data;
        }
    }

    public void WriteOAM(byte data)
    {
        _oam[_oamAddr++] = data;
    }

    private void IncrementScrollX()
    {
        if ((_mask & 0x18) != 0)
        {
            if ((_vramAddr & 0x001F) == 31)
            {
                _vramAddr &= 0xFFE0;
                _vramAddr ^= 0x0400;
            }
            else
            {
                _vramAddr++;
            }
        }
    }

    private void IncrementScrollY()
    {
        if ((_mask & 0x18) != 0)
        {
            if ((_vramAddr & 0x7000) != 0x7000)
            {
                _vramAddr += 0x1000;
            }
            else
            {
                _vramAddr &= 0x0FFF;
                int y = (_vramAddr & 0x03E0) >> 5;
                if (y == 29)
                {
                    y = 0;
                    _vramAddr ^= 0x0800;
                }
                else if (y == 31)
                {
                    y = 0;
                }
                else
                {
                    y++;
                }
                _vramAddr = (ushort)((_vramAddr & ~0x03E0) | (y << 5));
            }
        }
    }

    private void TransferAddressX()
    {
        if ((_mask & 0x18) != 0)
        {
            _vramAddr = (ushort)((_vramAddr & 0xFBE0) | (_tempVramAddr & 0x041F));
        }
    }

    private void TransferAddressY()
    {
        if ((_mask & 0x18) != 0)
        {
            _vramAddr = (ushort)((_vramAddr & 0x841F) | (_tempVramAddr & 0x7BE0));
        }
    }

    private void LoadBackgroundShifters()
    {
        _bgShifterPatternLo = (ushort)((_bgShifterPatternLo & 0xFF00) | _nextPatternLo);
        _bgShifterPatternHi = (ushort)((_bgShifterPatternHi & 0xFF00) | _nextPatternHi);
        _bgShifterPaletteLo = (ushort)((_bgShifterPaletteLo & 0xFF00) | ((_nextPalette & 0x01) != 0 ? 0xFF : 0x00));
        _bgShifterPaletteHi = (ushort)((_bgShifterPaletteHi & 0xFF00) | ((_nextPalette & 0x02) != 0 ? 0xFF : 0x00));
    }

    private void UpdateShifters()
    {
        if ((_mask & 0x18) != 0)
        {
            _bgShifterPatternLo <<= 1;
            _bgShifterPatternHi <<= 1;
            _bgShifterPaletteLo <<= 1;
            _bgShifterPaletteHi <<= 1;
        }
    }

    private void RenderPixel(int x, int y)
    {
        byte bgPixel = 0;
        byte bgPalette = 0;

        if ((_mask & 0x08) != 0)
        {
            ushort bitMux = (ushort)(0x8000 >> _fineX);
            byte p0_pixel = (byte)((_bgShifterPatternLo & bitMux) != 0 ? 1 : 0);
            byte p1_pixel = (byte)((_bgShifterPatternHi & bitMux) != 0 ? 1 : 0);
            bgPixel = (byte)((p1_pixel << 1) | p0_pixel);

            byte p0_pal = (byte)((_bgShifterPaletteLo & bitMux) != 0 ? 1 : 0);
            byte p1_pal = (byte)((_bgShifterPaletteHi & bitMux) != 0 ? 1 : 0);
            bgPalette = (byte)((p1_pal << 1) | p0_pal);
        }

        byte bgColourIndex;
        if (bgPixel == 0)
            bgColourIndex = PpuRead(0x3F00);
        else
            bgColourIndex = PpuRead((ushort)(0x3F00 + (bgPalette << 2) + bgPixel));

        uint finalColour = SystemColourPalette[bgColourIndex & 0x3F];

        if (TryGetSpritePixel(x, y, out byte spPalette, out bool spPriority, out byte spPixel, out bool isSpriteZero))
        {
            if (bgPixel != 0 && spPixel != 0 && isSpriteZero)
            {
                if ((_mask & 0x18) == 0x18 && x != 255)
                {
                    _status |= 0x40;
                }
            }

            if (bgPixel == 0 || !spPriority)
            {
                byte spColourIndex = PpuRead((ushort)(0x3F10 + (spPalette << 2) + spPixel));
                finalColour = SystemColourPalette[spColourIndex & 0x3F];
            }
        }

        screenBuffer[y * 256 + x] = ToRgba(finalColour);
    }

    public void CatchUp(int ppuCycles)
    {
        for (int i = 0; i < ppuCycles; i++)
        {
            if (scanline >= -1 && scanline < 240)
            {
                if (scanline == 0 && cycle == 0) cycle = 1;

                if (scanline == -1 && cycle == 1)
                {
                    _status &= 0x3F;
                }

                if ((cycle >= 2 && cycle < 258) || (cycle >= 321 && cycle < 338))
                {
                    UpdateShifters();

                    switch ((cycle - 1) % 8)
                    {
                        case 0:
                            LoadBackgroundShifters();
                            ushort ntAddr = (ushort)(0x2000 | (_vramAddr & 0x0FFF));
                            _nextTileId = PpuRead(ntAddr);
                            break;
                        case 2:
                            ushort attrAddr = (ushort)(0x23C0 | (_vramAddr & 0x0C00) | ((_vramAddr >> 4) & 0x38) | ((_vramAddr >> 2) & 0x07));
                            byte attrByte = PpuRead(attrAddr);
                            bool rightHalf = (_vramAddr & 0x02) != 0;
                            bool bottomHalf = (_vramAddr & 0x40) != 0;
                            int attrShift = (bottomHalf ? 4 : 0) + (rightHalf ? 2 : 0);
                            _nextPalette = (byte)((attrByte >> attrShift) & 0x03);
                            break;
                        case 4:
                            ushort patternBank = (ushort)((_ctrl & 0x10) != 0 ? 0x1000 : 0x0000);
                            int fineY = (_vramAddr >> 12) & 0x07;
                            ushort patternAddr = (ushort)(patternBank + _nextTileId * 16 + fineY);
                            _nextPatternLo = PpuRead(patternAddr);
                            break;
                        case 6:
                            ushort patternBankHi = (ushort)((_ctrl & 0x10) != 0 ? 0x1000 : 0x0000);
                            int fineYHi = (_vramAddr >> 12) & 0x07;
                            ushort patternAddrHi = (ushort)(patternBankHi + _nextTileId * 16 + fineYHi + 8);
                            _nextPatternHi = PpuRead(patternAddrHi);
                            break;
                        case 7:
                            IncrementScrollX();
                            break;
                    }
                }

                if (cycle == 256) IncrementScrollY();
                if (cycle == 257) 
                {
                    LoadBackgroundShifters();
                    TransferAddressX();
                }

                if (scanline == -1 && cycle >= 280 && cycle < 305)
                {
                    TransferAddressY();
                }

                if (scanline >= 0 && cycle >= 1 && cycle <= 256)
                {
                    RenderPixel(cycle - 1, scanline);
                }
            }

            if (scanline == 241 && cycle == 1)
            {
                _status |= 0x80;
                if ((_ctrl & 0x80) != 0) _bus.cpu.NMI();
            }

            cycle++;
            if (cycle >= 341)
            {
                cycle = 0;
                scanline++;
                if (scanline >= 261)
                {
                    scanline = -1;
                    frameComplete = true;
                }
            }
        }
    }
}

