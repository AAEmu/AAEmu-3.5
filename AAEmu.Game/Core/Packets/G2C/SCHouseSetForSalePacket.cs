using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCHouseSetForSalePacket(
    ushort tl,
    uint moneyAmount,
    uint sellToPlayerId,
    string sellToName,
    string houseName)
    : GamePacket(SCOffsets.SCHouseSetForSalePacket, 5)
{
    private const int MaxStringBytes = 128;

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(tl);
        stream.Write((int)moneyAmount);
        stream.Write(sellToPlayerId);
        stream.Write(TrimUtf8(sellToName, MaxStringBytes));
        stream.Write(TrimUtf8(houseName, MaxStringBytes));
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
