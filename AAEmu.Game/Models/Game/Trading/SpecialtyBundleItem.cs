using AAEmu.Game.Models.Game.Items.Templates;

namespace AAEmu.Game.Models.Game.Trading;

/// <summary>
/// Предмет торгового набора (specialty bundle) из таблицы <c>specialty_bundle_items</c> БД <c>compact.sqlite3</c>.
/// </summary>
public class SpecialtyBundleItem
{
    /// <summary>Идентификатор записи.</summary>
    public uint Id { get; set; }

    /// <summary>Идентификатор предмета (<c>item_id</c>).</summary>
    public uint ItemId { get; set; }

    /// <summary>Идентификатор торгового набора (<c>specialty_bundle_id</c>).</summary>
    public uint SpecialtyBundleId { get; set; }

    /// <summary>Прибыль от продажи данного предмета.</summary>
    public uint Profit { get; set; }

    /// <summary>Соотношение цены для данного предмета.</summary>
    public uint Ratio { get; set; }

    /// <summary>Ссылка на шаблон предмета (заполняется после загрузки <see cref="ItemManager"/>).</summary>
    public ItemTemplate Item { get; set; }
}
