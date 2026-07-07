using System.Numerics;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCHousingTradeListPacket(
    bool first,
    bool final,
    SCHousingTradeListPacket.HousingTradeInfo[] housingTradeInfos)
    : GamePacket(SCOffsets.SCHousingTradeListPacket, 5)
{
    private const int MaxHousingTradeInfoCount = 50;
    private const int MaxNameBytes = 128;

    public override PacketStream Write(PacketStream stream)
    {
        var count = housingTradeInfos.Length;
        if (count > MaxHousingTradeInfoCount)
            count = MaxHousingTradeInfoCount;

        stream.Write(count);
        stream.Write(first);
        stream.Write(final);

        for (var i = 0; i < count; i++)
        {
            stream.Write(housingTradeInfos[i].DecoLimit);
            stream.Write(housingTradeInfos[i].ExpandedDecoLimit);
            stream.Write(TrimUtf8(housingTradeInfos[i].Name, MaxNameBytes));
            stream.Write(housingTradeInfos[i].Tl);
            stream.Write(housingTradeInfos[i].Type);
            stream.Write(housingTradeInfos[i].Price);
            stream.WritePosition(housingTradeInfos[i].Pos);
            stream.Write(housingTradeInfos[i].ZoneId);
            stream.Write(housingTradeInfos[i].Category);
        }

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

    public readonly struct HousingTradeInfo(
        int decoLimit,
        int expandedDecoLimit,
        string name,
        ushort tl,
        uint type,
        long price,
        Vector3 pos,
        int zoneId,
        int category)
    {
        public int DecoLimit { get; } = decoLimit;
        public int ExpandedDecoLimit { get; } = expandedDecoLimit;
        public string Name { get; } = name;
        public ushort Tl { get; } = tl;
        public uint Type { get; } = type;
        public long Price { get; } = price;
        public Vector3 Pos { get; } = pos;
        public int ZoneId { get; } = zoneId;
        public int Category { get; } = category;
    }
}
