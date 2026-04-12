using System;

namespace nes;

public static class Helpers
{
    public static byte LowByte(ushort word) => (byte)(word & 0xFF);

    public static byte HighByte(ushort word) => (byte)((word >> 8) & 0xFF);

    public static ushort MakeWord(byte low, byte high) => (ushort)((high << 8) | low);
}
