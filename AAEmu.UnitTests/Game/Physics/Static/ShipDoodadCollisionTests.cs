#nullable enable
using System.Reflection;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Physics.Static;
using AAEmu.Geometry.Abstractions;
using AAEmu.UnitTests.Geometry;
using Microsoft.Data.Sqlite;
using System.Text;

namespace AAEmu.UnitTests.Game.Physics.Static;

public class ShipDoodadCollisionTests
{
    private sealed class HullFiles : IGeometryFileSource
    {
        public byte[]? TryReadBytes(string path) => path switch
        {
            "game/prefabs/hull.xml" => Encoding.UTF8.GetBytes("<Prefabs><Prefab Name='ship'><Objects><Object Type='Brush' Name='body' Prefab='objects/test.cgf' Scale='3,2,1' Rotate='0.70710678,0,0,0.70710678'/><Object Type='Brush' Name='sail' Prefab='objects/test.cgf' Scale='30,30,30'/></Objects></Prefab></Prefabs>"),
            "game/objects/test.cgf" => CgfFixture.WithBoxProxy(),
            _ => null
        };
    }

    private sealed class VariantHullFiles : IGeometryFileSource
    {
        public byte[]? TryReadBytes(string path) => path switch
        {
            "game/prefabs/variants.xml" => Encoding.UTF8.GetBytes("<Prefabs>" +
                "<Prefab Name='single'><Objects><Object Type='Brush' Name='speedboat_new_1' Prefab='objects/env/06_unit/01_ship/test.cgf'/></Objects></Prefab>" +
                "<Prefab Name='named'><Objects><Object Type='Brush' Name='galleon_test_body_1' Prefab='objects/env/06_unit/01_ship/galleon_body.cgf'/><Object Type='Brush' Name='rudder' Prefab='objects/env/06_unit/01_ship/rudder.cgf'/></Objects></Prefab>" +
                "<Prefab Name='animated'><Objects><Object Type='Entity' Name='body' EntityClass='AnimObject'><Properties object_Model='objects/env/06_unit/01_ship/fishboat.chr'/></Object><Object Type='Brush' Name='fish' Prefab='objects/env/06_unit/01_ship/fish.cgf'/></Objects></Prefab>" +
                "<Prefab Name='ambiguous'><Objects><Object Type='Brush' Name='a' Prefab='objects/env/06_unit/01_ship/a.cgf'/><Object Type='Brush' Name='b' Prefab='objects/env/06_unit/01_ship/b.cgf'/></Objects></Prefab>" +
                "<Prefab Name='decoration'><Objects><Object Type='Brush' Name='pack' Prefab='objects/env/01_nuia/backpack.cgf'/></Objects></Prefab></Prefabs>"),
            _ when path.EndsWith(".cgf") || path.EndsWith(".chr") => CgfFixture.WithBoxProxy(),
            _ => null
        };
    }

    [Test]
    public async Task HullBounds_RecognizesUnambiguousShipBodiesButRejectsDecorationAndAmbiguity()
    {
        var resolver = new ShipHullGeometryResolver(new VariantHullFiles());
        var single = await resolver.ResolveAsync("prefab://prefabs/variants.xml/single");
        var named = await resolver.ResolveAsync("prefab://prefabs/variants.xml/named");
        var animated = await resolver.ResolveAsync("prefab://prefabs/variants.xml/animated");
        var ambiguous = await resolver.ResolveAsync("prefab://prefabs/variants.xml/ambiguous");
        var decoration = await resolver.ResolveAsync("prefab://prefabs/variants.xml/decoration");
        await Assert.That(single.Bounds.X).IsEqualTo(2f).Within(0.001f);
        await Assert.That(named.Bounds.X).IsEqualTo(2f).Within(0.001f);
        await Assert.That(animated.Bounds.X).IsEqualTo(2f).Within(0.001f);
        await Assert.That(ambiguous.Bounds.X).IsEqualTo(0f);
        await Assert.That(decoration.Bounds.X).IsEqualTo(0f);
    }

    [Test]
    public async Task HullBounds_UsesOnlyBodyAndAppliesItsTransform()
    {
        var resolver = new ShipHullGeometryResolver(new HullFiles());
        var geometry = await resolver.ResolveAsync("prefab://prefabs/hull.xml/ship");
        await Assert.That(geometry.Bounds.X).IsEqualTo(4f).Within(0.001f);
        await Assert.That(geometry.Bounds.Y).IsEqualTo(6f).Within(0.001f);
    }

    private sealed class Files : IGeometryFileSource
    {
        public byte[]? TryReadBytes(string path) => path switch
        {
            "game/objects/test.cgf" => CgfFixture.WithBoxProxy(),
            "game/objects/test.chr" => CgfFixture.WithRenderOnly(),
            _ => null
        };
    }

    private sealed class PrefabFiles : IGeometryFileSource
    {
        public byte[]? TryReadBytes(string path) => path == "game/prefabs/test.xml"
            ? Encoding.UTF8.GetBytes("<Prefabs><Prefab Name='gate'><Objects><Object Type='Entity' EntityClass='AnimObject' Pos='1,2,3'><Properties object_Model='game/objects/gate.cga'/></Object></Objects></Prefab></Prefabs>")
            : null;
    }

    [Test]
    public async Task Prefab_ResolvesAnimatedEntityModelAndItsTransform()
    {
        var parts = new ShipDoodadModelResolver(new PrefabFiles()).Resolve("prefab://prefabs/test.xml/gate");
        await Assert.That(parts.Count).IsEqualTo(1);
        await Assert.That(parts[0].AssetPath).IsEqualTo("game/objects/gate.cga");
        await Assert.That(parts[0].Local.Translation.Z).IsEqualTo(3f);
    }

    [Test]
    public async Task CharacterWithoutProxy_UsesItsRenderMesh()
    {
        using var db = new SqliteConnection("Data Source=:memory:");
        db.Open();
        using var sql = db.CreateCommand();
        sql.CommandText = "CREATE TABLE doodad_almighties(id INTEGER, model TEXT, collide_ship INTEGER, no_collision INTEGER);" +
            "CREATE TABLE doodad_func_groups(id INTEGER, doodad_almighty_id INTEGER, model TEXT);" +
            "INSERT INTO doodad_almighties VALUES(1, 'cgf://objects/test.chr', 1, 0);";
        sql.ExecuteNonQuery();
        var registry = new ShipDoodadCollision(new Files());
        registry.SetCatalog(ShipDoodadModelCatalog.Load(db));
        var doodad = new Doodad { ObjId = 11, TemplateId = 1 };
        registry.Upsert(1, doodad);
        await Assert.That(SpinWait.SpinUntil(() => registry.Current(1).Instances == 1, 3000)).IsTrue();
    }

    [Test]
    public async Task PhaseAndRemoval_RebuildOnlyTheOwningWorld()
    {
        using var db = new SqliteConnection("Data Source=:memory:");
        db.Open();
        using var sql = db.CreateCommand();
        sql.CommandText = "CREATE TABLE doodad_almighties(id INTEGER, model TEXT, collide_ship INTEGER, no_collision INTEGER);" +
            "CREATE TABLE doodad_func_groups(id INTEGER, doodad_almighty_id INTEGER, model TEXT);" +
            "INSERT INTO doodad_almighties VALUES(1, 'cgf://objects/test.cgf', 1, 0);" +
            "INSERT INTO doodad_func_groups VALUES(2, 1, 'a://invalid');";
        sql.ExecuteNonQuery();
        var registry = new ShipDoodadCollision(new Files());
        registry.SetCatalog(ShipDoodadModelCatalog.Load(db));
        var first = new Doodad { ObjId = 11, TemplateId = 1 };
        first.Transform.Local.SetPosition(100f, 100f, 10f);
        var second = new Doodad { ObjId = 12, TemplateId = 1 };
        second.Transform.Local.SetPosition(200f, 200f, 10f);
        registry.Upsert(1, first);
        registry.Upsert(2, second);
        await Assert.That(SpinWait.SpinUntil(() => registry.Current(1).Instances == 1 && registry.Current(2).Instances == 1, 3000)).IsTrue();
        await Assert.That(registry.Current(1).TryFloor(100f, 100f, 20f, 0f, out var floor)).IsTrue();
        await Assert.That(floor).IsEqualTo(11f).Within(0.01f);

        typeof(Doodad).GetField("_funcGroupId", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(first, 2u);
        registry.Upsert(1, first);
        await Assert.That(SpinWait.SpinUntil(() => registry.Current(1).Instances == 0, 3000)).IsTrue();
        await Assert.That(registry.Current(2).Instances).IsEqualTo(1);
        registry.Remove(2, second.ObjId);
        await Assert.That(SpinWait.SpinUntil(() => registry.Current(2).Instances == 0, 3000)).IsTrue();
    }
}
