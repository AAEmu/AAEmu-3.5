using System.Numerics;

using AAEmu.Game.Physics.Static;
using AAEmu.Geometry;

namespace AAEmu.UnitTests.Game.Physics.Static;

/// <summary>
/// План W4 (порт <c>WalkerCollision</c>): стена обрывает шаг за радиус тела, остаток шага скользит вдоль неё,
/// низкая ступень стеной не считается, а «рядом ничего нет» избавляет от лучей на открытой земле.
/// </summary>
public class WalkerCollisionTests
{
    [Test]
    public async Task Probe_WhenCollisionIndexChanges_RechecksForNearbyWall()
    {
        var probe = new WalkerCollision.Probe();
        var from = new Vector3(103f, 100f, 50f);
        var to = new Vector3(103.2f, 100f, 50f);
        await Assert.That(probe.Clear(StaticCollision.Empty, from, to, 0.45f)).IsTrue();
        await Assert.That(probe.Clear(Wall(), from, to, 0.45f)).IsFalse();
    }

    private static CollisionMesh Box(Vector3 min, Vector3 max)
    {
        var v = new List<Vector3>();
        for (var i = 0; i < 8; i++)
            v.Add(new Vector3((i & 1) != 0 ? max.X : min.X, (i & 2) != 0 ? max.Y : min.Y, (i & 4) != 0 ? max.Z : min.Z));
        int[] t = [0, 2, 3, 0, 3, 1, 4, 5, 7, 4, 7, 6, 0, 1, 5, 0, 5, 4, 2, 6, 7, 2, 7, 3, 0, 4, 6, 0, 6, 2, 1, 3, 7, 1, 7, 5];
        return CollisionMesh.Build(v, t);
    }

    /// <summary>Стена 0,3 м толщиной от x = 105 вдоль y, высотой 0,9 м над землёй на 50 (двор зоны 333).</summary>
    private static StaticCollision Wall(float height = 0.9f) =>
        StaticCollision.FromInstances([(Box(new Vector3(105f, 80f, 50f), new Vector3(105.3f, 120f, 50f + height)), Matrix4x4.Identity)]);

    [Test]
    public async Task Clip_StraightIntoAWall_StopsARadiusShort()
    {
        var at = WalkerCollision.Clip(Wall(), new Vector3(104f, 100f, 50f), new Vector3(106f, 100f, 50f), 0.45f, out var blocked);
        await Assert.That(blocked).IsTrue();
        await Assert.That(at.X).IsEqualTo(104.55f).Within(0.01f);
        await Assert.That(at.Y).IsEqualTo(100f).Within(0.01f);
    }

    [Test]
    public async Task Clip_AtAnAngle_SlidesAlongTheWall()
    {
        // 45° в стену с 0,45 м от неё: поперёк идти некуда, вдоль стены остаток шага проходит.
        var at = WalkerCollision.Clip(Wall(), new Vector3(104.55f, 100f, 50f), new Vector3(104.85f, 100.3f, 50f), 0.45f, out var blocked);
        await Assert.That(blocked).IsTrue();
        await Assert.That(at.X).IsEqualTo(104.55f).Within(0.01f);
        await Assert.That(at.Y).IsEqualTo(100.3f).Within(0.01f);
    }

    [Test]
    public async Task Clip_OpenGroundAndLowSteps_AreNoWall()
    {
        var open = WalkerCollision.Clip(Wall(), new Vector3(100f, 100f, 50f), new Vector3(101f, 100f, 50f), 0.45f, out var blockedOpen);
        await Assert.That(blockedOpen).IsFalse();
        await Assert.That(open).IsEqualTo(new Vector3(101f, 100f, 50f));
        // Бордюр 0,4 м ниже луча колена: юнит на него поднимается, это не стена.
        WalkerCollision.Clip(Wall(0.4f), new Vector3(104f, 100f, 50f), new Vector3(106f, 100f, 50f), 0.45f, out var blockedKerb);
        await Assert.That(blockedKerb).IsFalse();
        // Уходить от стены — свободно.
        WalkerCollision.Clip(Wall(), new Vector3(104.6f, 100f, 50f), new Vector3(103f, 100f, 50f), 0.45f, out var blockedAway);
        await Assert.That(blockedAway).IsFalse();
    }

    [Test]
    public async Task Clip_WalkingParallelToTheWall_IsNotShortened()
    {
        // Вдоль стены, в 0,45 м от неё: ни поперёк, ни наискось движения в стену нет.
        var at = WalkerCollision.Clip(Wall(), new Vector3(104.55f, 90f, 50f), new Vector3(104.55f, 92f, 50f), 0.45f, out var blocked);
        await Assert.That(blocked).IsFalse();
        await Assert.That(at.Y).IsEqualTo(92f).Within(0.01f);
    }

    [Test]
    public async Task Probe_SkipsTheRaysAwayFromBrushes_AndAsksAgainNearOne()
    {
        var wall = Wall();
        await Assert.That(wall.AnyBoxOverlaps(new Vector3(90f, 90f, 50f), new Vector3(100f, 110f, 52f))).IsFalse();
        await Assert.That(wall.AnyBoxOverlaps(new Vector3(104f, 99f, 50.5f), new Vector3(106f, 101f, 51f))).IsTrue();
        // Выше верхнего края стены (0,9 м) коробка чистая.
        await Assert.That(wall.AnyBoxOverlaps(new Vector3(104f, 99f, 51f), new Vector3(106f, 101f, 52f))).IsFalse();

        var probe = new WalkerCollision.Probe();
        // 10 м от стены: чисто, и дальше по ходу — тоже, без нового запроса.
        await Assert.That(probe.Clear(wall, new Vector3(95f, 100f, 50f), new Vector3(95.2f, 100f, 50f), 0.45f)).IsTrue();
        await Assert.That(probe.Clear(wall, new Vector3(96f, 100f, 50f), new Vector3(96.2f, 100f, 50f), 0.45f)).IsTrue();
        // В трёх метрах от стены: не чисто, решают лучи.
        await Assert.That(probe.Clear(wall, new Vector3(103f, 100f, 50f), new Vector3(103.2f, 100f, 50f), 0.45f)).IsFalse();
        // Пустой мир чист всегда.
        await Assert.That(new WalkerCollision.Probe().Clear(StaticCollision.Empty, new Vector3(103f, 100f, 50f), new Vector3(103.2f, 100f, 50f), 0.45f)).IsTrue();
    }

    [Test]
    public async Task Radius_IsClampedToNativesStopDistances()
    {
        await Assert.That(WalkerCollision.Radius(0.1f)).IsEqualTo(WalkerCollision.MinRadiusMetres);
        await Assert.That(WalkerCollision.Radius(0.45f)).IsEqualTo(0.45f);
        await Assert.That(WalkerCollision.Radius(2.5f)).IsEqualTo(WalkerCollision.MaxRadiusMetres);
        await Assert.That(WalkerCollision.KneeMetres).IsGreaterThan(NpcGroundResolver.StepUpMetres);
    }

    [Test]
    public async Task Clip_EmptyIndex_LeavesTheStepAlone()
    {
        // Пока зона ещё читается в фоне, индекс пуст: шаг не трогаем (мягкая деградация слоя).
        var at = WalkerCollision.Clip(StaticCollision.Empty, new Vector3(104f, 100f, 50f), new Vector3(106f, 100f, 50f), 0.45f, out var blocked);
        await Assert.That(blocked).IsFalse();
        await Assert.That(at).IsEqualTo(new Vector3(106f, 100f, 50f));
    }
}
