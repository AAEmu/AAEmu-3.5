using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Items.Templates;

namespace AAEmu.Game.Models.Game.Crafts;

/// <summary>
/// Рецепт крафта из таблицы <c>crafts</c> БД <c>compact.sqlite3</c>.
/// </summary>
/// <remarks>
/// Схема таблицы <c>crafts</c>:
/// <list type="bullet">
///   <item><description><c>id</c> int PRIMARY KEY → <see cref="Id"/></description></item>
///   <item><description><c>cast_delay</c> int → <see cref="CastDelay"/></description></item>
///   <item><description><c>tool_id</c> int → <see cref="ToolId"/></description></item>
///   <item><description><c>skill_id</c> int → <see cref="SkillId"/></description></item>
///   <item><description><c>wi_id</c> int → <see cref="WiId"/></description></item>
///   <item><description><c>req_doodad_id</c> int → <see cref="ReqDoodadId"/></description></item>
///   <item><description><c>need_bind</c> bool → <see cref="NeedBind"/></description></item>
///   <item><description><c>ac_id</c> int → <see cref="AcId"/></description></item>
///   <item><description><c>actability_limit</c> int → <see cref="ActabilityLimit"/></description></item>
///   <item><description><c>show_upper_crafts</c> bool → <see cref="ShowUpperCraft"/></description></item>
///   <item><description><c>recommend_level</c> int → <see cref="RecommendLevel"/></description></item>
///   <item><description><c>visible_order</c> int → <see cref="VisibleOrder"/></description></item>
///   <item><description><c>desc</c> text → <see cref="Desc"/></description></item>
///   <item><description><c>products_pack_id</c> int → <see cref="ProductsPackId"/></description></item>
///   <item><description><c>title</c> text → <see cref="Title"/></description></item>
///   <item><description><c>use_only_actability</c> bool → <see cref="UseOnlyActability"/></description></item>
/// </list>
/// </remarks>
public class Craft
{
    public uint Id { get; set; }
    public int CastDelay { get; set; }
    public uint ToolId { get; set; }
    public uint SkillId { get; set; }
    public uint WiId { get; set; }
    public uint ReqDoodadId { get; set; }
    public bool NeedBind { get; set; }
    public uint AcId { get; set; }
    public int ActabilityLimit { get; set; }
    public bool ShowUpperCraft { get; set; }
    public int RecommendLevel { get; set; }
    public int VisibleOrder { get; set; }
    public string Desc { get; set; } = string.Empty;
    public uint ProductsPackId { get; set; }
    public string Title { get; set; } = string.Empty;
    public bool UseOnlyActability { get; set; }

    public List<CraftProduct> CraftProducts { get; set; } = [];
    public List<CraftMaterial> CraftMaterials { get; set; } = [];
    public bool IsPack { get; set; }

    public bool ResultsInBackpack
    {
        get
        {
            return CraftProducts.Select(product => ItemManager.Instance.GetTemplate(product.ItemId))
                .OfType<BackpackTemplate>().Any();
        }
    }
}
