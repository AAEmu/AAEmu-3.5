namespace AAEmu.Game.Models.Game.Items;

/// <summary>
/// Стартовый предмет для способности из таблицы <c>character_supplies</c> БД <c>compact.sqlite3</c>.
/// </summary>
/// <remarks>
/// В текущей реализации свойство <see cref="Id"/> дублирует <see cref="ItemId"/>
/// для совместимости с кодом выдачи предметов.
/// </remarks>
public class AbilitySupplyItem
{
    /// <summary>Идентификатор записи в таблице <c>character_supplies</c>.</summary>
    public uint Id { get; set; }

    /// <summary>Идентификатор способности (<c>ability_id</c>).</summary>
    public uint AbilityId { get; set; }

    /// <summary>Идентификатор предмета (<c>item_id</c>), совпадает с <see cref="Id"/>.</summary>
    public uint ItemId { get; set; }

    /// <summary>Количество предметов (<c>amount</c>).</summary>
    public int Amount { get; set; }

    /// <summary>Качество предмета (<c>grade_id</c>).</summary>
    public byte Grade { get; set; }
}
