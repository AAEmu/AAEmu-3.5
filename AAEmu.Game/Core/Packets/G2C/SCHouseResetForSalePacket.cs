using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCHouseResetForSalePacket(ushort tl, string houseName) : GamePacket(SCOffsets.SCHouseResetForSalePacket, 5)
{
    private const int MaxHouseNameBytes = 128;

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(tl);
        stream.Write(TrimUtf8(houseName, MaxHouseNameBytes));
        return stream;
    }

    private static string TrimUtf8(string value, int maxBytes)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        if (System.Text.Encoding.UTF8.GetByteCount(value) <= maxBytes)
            return value;

        var length = value.Length;
        while (length > 0)
        {
            length--;
            if (char.IsLowSurrogate(value[length]) && length > 0)
                length--;
            if (length > 0 && char.IsHighSurrogate(value[length - 1]))
            {
                length--;
                continue;
            }

            var result = value[..length];
            if (System.Text.Encoding.UTF8.GetByteCount(result) <= maxBytes)
                return result;
        }

        return "";
    }
}
