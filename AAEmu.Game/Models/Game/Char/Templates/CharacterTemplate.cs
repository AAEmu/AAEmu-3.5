using AAEmu.Game.Models.Game.World.Transform;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Models.Game.Char.Templates;

/// <summary>
/// Шаблон стартового персонажа из таблицы <c>characters</c> БД <c>compact.sqlite3</c>.
/// </summary>
public class CharacterTemplate
{
    public Race Race { get; set; }
    public Gender Gender { get; set; }
    public uint ModelId { get; set; }
    public uint ZoneId { get; set; }
    public FactionsEnum FactionId { get; set; }
    public uint ReturnDistrictId { get; set; }
    public uint ResurrectionDistrictId { get; set; }

    /// <summary>Может ли игрок создать персонажа данного типа (<c>creatable</c>).</summary>
    public bool Creatable { get; set; }

    /// <summary>Идентификатор набора кастомизации по умолчанию (<c>default_custom_id</c>).</summary>
    public uint DefaultCustomId { get; set; }

    /// <summary>Идентификатор звукового пака голоса в бою (<c>default_fx_voice_sound_pack_id</c>).</summary>
    public uint DefaultFxVoiceSoundPackId { get; set; }

    /// <summary>Идентификатор системного звукового пака голоса (<c>default_system_voice_sound_pack_id</c>).</summary>
    public uint DefaultSystemVoiceSoundPackId { get; set; }

    /// <summary>Идентификатор предмета лица (<c>face_item_id</c>).</summary>
    public uint FaceItemId { get; set; }

    /// <summary>Идентификатор cloth-pack для предпросмотра в боевой стойке (<c>fight_preview_cloth_pack_id</c>).</summary>
    public uint FightPreviewClothPackId { get; set; }

    /// <summary>Идентификатор weapon-pack для предпросмотра в боевой стойке (<c>fight_preview_weapon_pack_id</c>).</summary>
    public uint FightPreviewWeaponPackId { get; set; }

    /// <summary>Идентификатор cloth-pack для предпросмотра в любовной стойке (<c>love_preview_cloth_pack_id</c>).</summary>
    public uint LovePreviewClothPackId { get; set; }

    /// <summary>Идентификатор weapon-pack для предпросмотра в любовной стойке (<c>love_preview_weapon_pack_id</c>).</summary>
    public uint LovePreviewWeaponPackId { get; set; }

    /// <summary>Идентификатор cloth-pack для предпросмотра в магической стойке (<c>magic_preview_cloth_pack_id</c>).</summary>
    public uint MagicPreviewClothPackId { get; set; }

    /// <summary>Идентификатор weapon-pack для предпросмотра в магической стойке (<c>magic_preview_weapon_pack_id</c>).</summary>
    public uint MagicPreviewWeaponPackId { get; set; }

    /// <summary>Идентификатор cloth-pack для предпросмотра по умолчанию (<c>preview_cloth_pack_id</c>).</summary>
    public uint PreviewClothPackId { get; set; }

    /// <summary>Идентификатор cloth-pack для предпросмотра в дикой стойке (<c>wild_preview_cloth_pack_id</c>).</summary>
    public uint WildPreviewClothPackId { get; set; }

    public WorldSpawnPosition SpawnPosition { get; set; } = new();
    public uint[] Items { get; set; } = new uint[7];
    public List<uint> Buffs { get; set; } = [];
    public byte NumInventorySlot { get; set; }
    public short NumBankSlot { get; set; }

    public CharacterTemplate()
    {
        SpawnPosition = new WorldSpawnPosition();
        Items = new uint[7];
        Buffs = [];
    }
}
