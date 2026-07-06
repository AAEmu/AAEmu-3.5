namespace AAEmu.Game.Models.Game.Char.Templates;

/// <summary>
/// Категория жизненных умений из таблицы <c>actability_categories</c> БД <c>compact.sqlite3</c>.
/// </summary>
public class ActabilityCategoriesTemplate
{
    /// <summary>Идентификатор категории.</summary>
    public uint Id { get; set; }

    /// <summary>Название категории.</summary>
    public string Name { get; set; }

    /// <summary>Идентификатор группы (<c>group_id</c>).</summary>
    public uint GroupId { get; set; }

    /// <summary>Видима ли категория в интерфейсе (<c>visible_ui</c>).</summary>
    public bool VisibleUi { get; set; }

    /// <summary>Порядок отображения категории.</summary>
    public int VisibleOrder { get; set; }
}
