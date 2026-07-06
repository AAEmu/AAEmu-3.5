using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.World;

/// <summary>
/// Форма области действия из таблицы <c>aoe_shapes</c> БД <c>compact.sqlite3</c>.
/// </summary>
public class AreaShape
{
    /// <summary>Идентификатор формы.</summary>
    public uint Id { get; set; }

    /// <summary>Тип формы (<c>kind_id</c>): сфера или кубоид.</summary>
    public AreaShapeType Type { get; set; }

    /// <summary>Первый размерный параметр формы.</summary>
    public float Value1 { get; set; }

    /// <summary>Второй размерный параметр формы.</summary>
    public float Value2 { get; set; }

    /// <summary>Третий размерный параметр формы (например, высота).</summary>
    public float Value3 { get; set; }

    /// <summary>Указывает, корректируется ли угол применения формы (<c>adjust_angle</c>).</summary>
    public bool AdjustAngle { get; set; }

    /// <summary>Целевой тип области действия (<c>area_target_kind_id</c>).</summary>
    public uint AreaTargetKindId { get; set; }

    /// <summary>Указывает, рассчитывается ли дистанция до цели (<c>calc_distance</c>).</summary>
    public bool CalcDistance { get; set; }

    public List<T> ComputeCuboid<T>(GameObject origin, List<T> toCheck) where T : GameObject
    {
        // Z check
        var zOffset = Value3;
        toCheck = toCheck.Where(o => o.Transform.World.Position.Z >= origin.Transform.World.Position.Z - zOffset && o.Transform.World.Position.Z <= origin.Transform.World.Position.Z + zOffset).ToList();
        if (toCheck.Count == 0)
            return toCheck;

        // Triangle check
        var vertices = MathUtil.GetCuboidVertices(Value1, Value2,
            origin.Transform.World.Position.X, origin.Transform.World.Position.Y,
            //origin.Transform.World.ToRollPitchYawSBytes().Item3);
            origin.Transform.World.Rotation.Z);

        toCheck = toCheck.Where(o =>
        {
            var tri1 = MathUtil.PointInTriangle((o.Transform.World.Position.X, o.Transform.World.Position.Y), vertices[0], vertices[1],
                vertices[2]);

            var tri2 = MathUtil.PointInTriangle((o.Transform.World.Position.X, o.Transform.World.Position.Y), vertices[1], vertices[2],
                vertices[3]);

            return tri1 || tri2;
        }).ToList();

        return toCheck;
    }
}
