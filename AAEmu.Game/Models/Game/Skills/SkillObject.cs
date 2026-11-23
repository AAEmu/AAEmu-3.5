using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;

namespace AAEmu.Game.Models.Game.Skills;

public enum SkillObjectType
{
    None = 0,
    Unk1 = 1,
    Unk2 = 2,
    Unk3 = 3,
    Unk4 = 4,
    Unk5 = 5,
    Unk6 = 6,
    ItemGradeEnchantingSupport = 7,
    SpecialAbility = 18
}

public class SkillObject : PacketMarshaler
{
    private const byte WireTypeMask = 0x3F;
    private const byte WireFlagMask = 0x80;
    private byte _wireFlag;

    internal bool IsItemProc { get; init; }
    public SkillObjectType Flag { get; set; } = SkillObjectType.None;

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((byte)((byte)Flag | _wireFlag));
        return stream;
    }

    internal static SkillObject ReadFrom(PacketStream stream, byte flag)
    {
        var type = (SkillObjectType)(flag & WireTypeMask);
        var result = GetByType(type);
        result._wireFlag = (byte)(flag & WireFlagMask);
        if (type != SkillObjectType.None)
            result.Read(stream);
        return result;
    }

    public static SkillObject GetByType(SkillObjectType flag)
    {
        SkillObject obj;
        switch (flag)
        {
            case SkillObjectType.Unk1: // TODO - Skills bound to portals
                obj = new SkillObjectUnk1();
                break;
            case SkillObjectType.Unk2: // TODO - Skills bound to home portals
                obj = new SkillObjectUnk2();
                break;
            case SkillObjectType.Unk3:
                obj = new SkillObjectUnk3();
                break;
            case SkillObjectType.Unk4:
                obj = new SkillObjectUnk4();
                break;
            case SkillObjectType.Unk5:
                obj = new SkillObjectUnk5();
                break;
            case SkillObjectType.Unk6:
                obj = new SkillObjectUnk6();
                break;
            case SkillObjectType.ItemGradeEnchantingSupport:
                obj = new SkillObjectItemGradeEnchantingSupport();
                break;
            case SkillObjectType.SpecialAbility:
                obj = new SkillObjectSpecialAbility();
                break;
            case SkillObjectType.None:
            default:
                obj = new SkillObject();
                break;
        }

        obj.Flag = flag;
        return obj;
    }
}

public sealed class SkillObjectSpecialAbility : SkillObject
{
    public uint Ability { get; set; }

    public SkillObjectSpecialAbility()
    {
        Flag = SkillObjectType.SpecialAbility;
    }

    public override void Read(PacketStream stream) => Ability = stream.ReadUInt32();

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);
        return stream.Write(Ability);
    }
}

public class SkillObjectUnk1 : SkillObject
{
    public byte Type { get; set; }
    public int Id { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }

    public override void Read(PacketStream stream)
    {
        Type = stream.ReadByte();
        Id = stream.ReadInt32();
        X = Helpers.ConvertLongX(stream.ReadInt64());
        Y = Helpers.ConvertLongX(stream.ReadInt64());
        Z = stream.ReadSingle();
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);
        stream.Write(Type);
        stream.Write(Id);
        stream.Write(Helpers.ConvertLongX(X));
        stream.Write(Helpers.ConvertLongX(Y));
        stream.Write(Z);
        return stream;
    }
}

public class SkillObjectUnk2 : SkillObject
{
    public int Id { get; set; }
    public string Name { get; set; }

    public override void Read(PacketStream stream)
    {
        Id = stream.ReadInt32();
        Name = stream.ReadString();
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);
        stream.Write(Id);
        stream.Write(Name);
        return stream;
    }
}

public class SkillObjectUnk3 : SkillObject
{
    public string Msg { get; set; }

    public override void Read(PacketStream stream)
    {
        Msg = stream.ReadString();
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);
        stream.Write(Msg);
        return stream;
    }
}

public class SkillObjectUnk4 : SkillObject
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }

    public override void Read(PacketStream stream)
    {
        X = Helpers.ConvertLongX(stream.ReadInt64());
        Y = Helpers.ConvertLongY(stream.ReadInt64());
        Z = stream.ReadSingle();
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);
        stream.Write(Helpers.ConvertLongX(X));
        stream.Write(Helpers.ConvertLongY(Y));
        stream.Write(Z);
        return stream;
    }
}

public class SkillObjectUnk5 : SkillObject
{
    public int Step { get; set; }

    public override void Read(PacketStream stream)
    {
        Step = stream.ReadInt32();
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);
        stream.Write(Step);
        return stream;
    }
}

public class SkillObjectUnk6 : SkillObject
{
    public string Name { get; set; }

    public override void Read(PacketStream stream)
    {
        Name = stream.ReadString();
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);
        stream.Write(Name);
        return stream;
    }
}

public class SkillObjectItemGradeEnchantingSupport : SkillObject
{
    public uint Id { get; set; }
    public ulong SupportItemId { get; set; }
    public bool AutoUseAaPoint { get; set; }

    public override void Read(PacketStream stream)
    {
        Id = stream.ReadUInt32();
        SupportItemId = stream.ReadUInt64();
        AutoUseAaPoint = stream.ReadBoolean();
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);
        stream.Write(Id);
        stream.Write(SupportItemId);
        stream.Write(AutoUseAaPoint);
        return stream;
    }
}
