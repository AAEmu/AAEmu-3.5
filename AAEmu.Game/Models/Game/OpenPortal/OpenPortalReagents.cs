namespace AAEmu.Game.Models.Game.OpenPortal;

/// <summary>
/// Реагент для открытия портала из таблиц
/// <c>open_portal_inland_reagents</c> и <c>open_portal_outland_reagents</c> БД <c>compact.sqlite3</c>.
/// </summary>
public class OpenPortalReagents
{
    /// <summary>Идентификатор записи.</summary>
    public uint Id { get; set; }

    /// <summary>Идентификатор эффекта открытия портала.</summary>
    public uint OpenPortalEffectId { get; set; }

    /// <summary>Идентификатор предмета-реагента.</summary>
    public uint ItemId { get; set; }

    /// <summary>Количество предмета.</summary>
    public int Amount { get; set; }

    /// <summary>Приоритет расходования (сначала удаляются предметы с большим приоритетом).</summary>
    public int Priority { get; set; }
}
