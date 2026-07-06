namespace AAEmu.Game.Models.Game.Char.Templates;

/// <summary>
/// Группа жизненных умений (actability) из таблицы <c>actability_groups</c> БД <c>compact.sqlite3</c>.
/// </summary>
public class ActabilityTemplate
{
    /// <summary>Идентификатор группы.</summary>
    public uint Id { get; set; }

    /// <summary>Название группы.</summary>
    public string Name { get; set; }

    /// <summary>Идентификатор связанного атрибута юнита.</summary>
    public int UnitAttributeId { get; set; }

    /// <summary>Путь к иконке группы.</summary>
    public string IconPath { get; set; }

    /// <summary>Отображается ли группа на странице навыков.</summary>
    public bool SkillPageVisible { get; set; }

    /// <summary>Общая видимость группы.</summary>
    public bool Visible { get; set; }
}
