using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Emotion;

/// <summary>
/// Шаблон текста эмоции/выражения из таблицы <c>express_texts</c> БД <c>compact.sqlite3</c>.
/// </summary>
/// <remarks>
/// Схема таблицы <c>express_texts</c>:
/// <list type="bullet">
///   <item><description><c>id</c> int PRIMARY KEY → <see cref="Id"/></description></item>
///   <item><description><c>anim_id</c> int → <see cref="AnimId"/></description></item>
///   <item><description><c>me</c> text → <see cref="Me"/></description></item>
///   <item><description><c>me_target</c> text → <see cref="MeTarget"/></description></item>
///   <item><description><c>npc_anim_id</c> int → <see cref="NpcAnimId"/></description></item>
///   <item><description><c>other</c> text → <see cref="Other"/></description></item>
///   <item><description><c>other_me</c> text → <see cref="OtherMe"/></description></item>
///   <item><description><c>other_target</c> text → <see cref="OtherTarget"/></description></item>
/// </list>
/// </remarks>
public class ExpressText : PacketMarshaler
{
    public uint Id { get; set; }
    public uint AnimId { get; set; }
    public string Me { get; set; } = string.Empty;
    public string MeTarget { get; set; } = string.Empty;
    public uint NpcAnimId { get; set; }
    public string Other { get; set; } = string.Empty;
    public string OtherMe { get; set; } = string.Empty;
    public string OtherTarget { get; set; } = string.Empty;
}
