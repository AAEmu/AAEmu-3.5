using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCRebuildHouseTaxInfoPacket(ushort tl, int dr, SCRebuildHouseTaxInfoPacket.TaxInfo[] taxInfos)
    : GamePacket(SCOffsets.SCRebuildHouseTaxInfoPacket, 5)
{
    private const int MaxTaxInfoCount = 100;

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(tl);
        stream.Write(dr);

        var count = taxInfos.Length;
        if (count > MaxTaxInfoCount)
            count = MaxTaxInfoCount;

        stream.Write(count);
        for (var i = 0; i < count; i++)
        {
            stream.Write(taxInfos[i].Bt);
            stream.Write(taxInfos[i].Vt);
            stream.Write(taxInfos[i].Pd);
            stream.Write(taxInfos[i].Wp);
            stream.Write(taxInfos[i].Dtr);
        }

        return stream;
    }

    public readonly struct TaxInfo(int bt, bool vt, DateTime pd, int wp, int dtr)
    {
        public int Bt { get; } = bt;
        public bool Vt { get; } = vt;
        public DateTime Pd { get; } = pd;
        public int Wp { get; } = wp;
        public int Dtr { get; } = dtr;
    }
}
