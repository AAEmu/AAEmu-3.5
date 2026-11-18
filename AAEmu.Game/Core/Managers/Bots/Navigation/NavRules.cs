namespace AAEmu.Game.Core.Managers.Bots.Navigation;

/// <summary>
/// The decisions of path-finding (DESIGN 8.3), kept apart from the search so they test on their own: which links a
/// walker may use, which water it may cross, and how two blocks join at their seam.
/// </summary>
public static class NavRules
{
    /// <summary>
    /// The node bits the client sets on a triangle inside a blocked area (a building's footprint, a rock): every link
    /// into such a triangle from outside is blocked in the file (<c>MaxPassRadius</c> -1), links out of it are not.
    /// </summary>
    public const byte ForbiddenFlags = 0x30;

    /// <summary>Node type of a triangle (DESIGN 8.3).</summary>
    public const byte TriangleNodeType = 2;

    /// <summary>
    /// Node type of a waypoint on a structure's walkable surface (gates, bridges, stairs): a grid of points about
    /// 1.7 m apart, joined to the triangles at the structure's entrances. Walked only with
    /// <c>Navigation.UseStructureGrids</c>.
    /// </summary>
    public const byte WaypointNodeType = 4;

    /// <summary>Pass radius of the join between two blocks' copies of the same waypoint: no limit.</summary>
    public const float SameWaypointRadius = float.MaxValue;

    /// <summary>Block sides, in the order <see cref="NavBlock.Seams"/> keeps them.</summary>
    public const int West = 0, East = 1, South = 2, North = 3;

    /// <summary>A seam stretch shorter than this, in metres, is not a way across.</summary>
    public const float MinSeamOverlap = 0.001f;

    public static bool IsForbidden(byte fileFlags) => (fileFlags & ForbiddenFlags) != 0;

    /// <summary>
    /// A link may be walked when it is not blocked (<c>MaxPassRadius</c> below 0 never is) and is at least as wide
    /// as the walker on each side.
    /// </summary>
    public static bool Passable(float maxPassRadius, float actorRadius) =>
        maxPassRadius >= 0f && maxPassRadius >= actorRadius;

    /// <summary>
    /// The file's own rule for blocked areas: never into one from outside, but out of one, or on inside it, so a
    /// walker standing in one can leave.
    /// </summary>
    public static bool MayEnter(bool fromForbidden, bool toForbidden) => fromForbidden || !toForbidden;

    /// <summary>
    /// The deepest water a search allows: the limit asked for, or the water the walker already stands in when that
    /// is deeper, so a swimming bot can always plan its way out.
    /// </summary>
    public static float EffectiveMaxDepth(float maxDepth, float startDepth) =>
        float.IsNaN(maxDepth) ? startDepth : MathF.Max(maxDepth, startDepth);

    /// <summary>A crossing into water <paramref name="depth"/> metres deep is allowed under <paramref name="maxDepth"/>.</summary>
    public static bool WaterAllows(float depth, float maxDepth) => depth <= maxDepth;

    /// <summary>Water depth over ground at <paramref name="groundZ"/> under a surface at <paramref name="surfaceZ"/>; 0 when dry.</summary>
    public static float Depth(float surfaceZ, float groundZ) =>
        float.IsNaN(surfaceZ) || surfaceZ <= groundZ ? 0f : surfaceZ - groundZ;

    /// <summary>
    /// How wide a walker may be to cross a seam stretch <paramref name="overlap"/> metres long: half of it, and no
    /// more than either block's own link across that edge allows (NaN where a block has none). A blocked edge in
    /// either block blocks the crossing, and so does entering a blocked area from outside.
    /// </summary>
    public static float SeamRadius(float overlap, float outRadius, float inRadius, bool fromForbidden, bool toForbidden)
    {
        if (!MayEnter(fromForbidden, toForbidden) || overlap < MinSeamOverlap)
            return -1f;
        if (outRadius < 0f || inRadius < 0f)
            return -1f;
        var radius = overlap / 2f;
        if (!float.IsNaN(outRadius))
            radius = MathF.Min(radius, outRadius);
        if (!float.IsNaN(inRadius))
            radius = MathF.Min(radius, inRadius);
        return radius;
    }

    /// <summary>
    /// The side of block (<paramref name="minX"/>, <paramref name="minY"/>) an edge from (ax, ay) to (bx, by) lies on,
    /// both ends within <paramref name="tolerance"/> metres of that side's line; -1 for none.
    /// </summary>
    public static int SideOf(float minX, float minY, float ax, float ay, float bx, float by, float tolerance)
    {
        var maxX = minX + NavBlock.Size;
        var maxY = minY + NavBlock.Size;
        if (MathF.Abs(ax - minX) <= tolerance && MathF.Abs(bx - minX) <= tolerance)
            return West;
        if (MathF.Abs(ax - maxX) <= tolerance && MathF.Abs(bx - maxX) <= tolerance)
            return East;
        if (MathF.Abs(ay - minY) <= tolerance && MathF.Abs(by - minY) <= tolerance)
            return South;
        if (MathF.Abs(ay - maxY) <= tolerance && MathF.Abs(by - maxY) <= tolerance)
            return North;
        return -1;
    }

    /// <summary>The side of the neighbour that faces <paramref name="side"/>.</summary>
    public static int Opposite(int side) => side ^ 1;

    /// <summary>Block step to the neighbour across <paramref name="side"/>.</summary>
    public static (int Dx, int Dy) Step(int side) => side switch
    {
        West => (-1, 0),
        East => (1, 0),
        South => (0, -1),
        _ => (0, 1),
    };

    /// <summary>
    /// Edge number of a triangle from two of its corner numbers (0..2): edge e joins corners e and (e + 1) % 3. -1
    /// when the pair is not an edge.
    /// </summary>
    public static int EdgeOfCorners(int start, int end) => (start, end) switch
    {
        (0, 1) or (1, 0) => 0,
        (1, 2) or (2, 1) => 1,
        (2, 0) or (0, 2) => 2,
        _ => -1,
    };
}
