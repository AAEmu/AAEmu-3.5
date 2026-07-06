namespace AAEmu.Game.Models.Game.Char;

/// <summary>
/// Лимит экспертизы из таблицы <c>expert_limits</c> БД <c>compact.sqlite3</c>.
/// </summary>
public class ExpertLimit
{
    /// <summary>Идентификатор записи.</summary>
    public uint Id { get; set; }

    /// <summary>Название лимита.</summary>
    public string Name { get; set; }

    /// <summary>Верхняя граница значения.</summary>
    public int UpLimit { get; set; }

    /// <summary>Количество доступных слотов экспертизы.</summary>
    public byte ExpertLimitCount { get; set; }

    /// <summary>Бонус к преимуществу.</summary>
    public int Advantage { get; set; }

    /// <summary>Бонус к скорости применения (cast advantage).</summary>
    public int CastAdvantage { get; set; }

    /// <summary>Цвет индикатора в формате ARGB.</summary>
    public uint ColorArgb { get; set; }

    /// <summary>Цвет шкалы.</summary>
    public uint GaugeColor { get; set; }

    /// <summary>Идентификатор валюты для повышения.</summary>
    public uint UpCurrencyId { get; set; }

    /// <summary>Цена повышения.</summary>
    public int UpPrice { get; set; }

    /// <summary>Идентификатор валюты для понижения.</summary>
    public uint DownCurrencyId { get; set; }

    /// <summary>Цена понижения.</summary>
    public int DownPrice { get; set; }

    /// <summary>Показывать ли лимит в интерфейсе.</summary>
    public bool Show { get; set; }

    /// <summary>Множитель опыта.</summary>
    public int ExpMul { get; set; }

    /// <summary>Разрешено ли усиление.</summary>
    public bool UseIntensified { get; set; }
}
