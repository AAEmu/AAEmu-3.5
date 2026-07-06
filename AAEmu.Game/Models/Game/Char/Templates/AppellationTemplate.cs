namespace AAEmu.Game.Models.Game.Char.Templates;

/// <summary>
/// Титул из таблицы <c>appellations</c> БД <c>compact.sqlite3</c>.
/// </summary>
public class AppellationTemplate
{
    /// <summary>Идентификатор титула.</summary>
    public uint Id { get; set; }

    /// <summary>Идентификатор накладываемого баффа.</summary>
    public uint BuffId { get; set; }

    /// <summary>Название титула.</summary>
    public string Name { get; set; }

    /// <summary>Применяется ли титул сразу при получении (<c>apply_appellation_at_once</c>).</summary>
    public bool ApplyAppellationAtOnce { get; set; }
}
