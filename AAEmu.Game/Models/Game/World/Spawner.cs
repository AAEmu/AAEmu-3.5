using System.Numerics;

using AAEmu.Game.Models.Game.World.Transform;

namespace AAEmu.Game.Models.Game.World;

public class Spawner<T> where T : GameObject
{
    public uint Id { get; set; }     // index
    public uint SpawnerId { get; set; } // spawner template id
    public uint UnitId { get; set; }    // npc template id
    public string FollowPath { get; set; } = string.Empty;
    public int PathPointNo { get; set; }
    /// <summary>Zone-local spawn point from <c>npc_spawners.g</c> (member position).</summary>
    public Vector3 PlacementSpawnLocal { get; set; } = new(float.NaN, float.NaN, float.NaN);
    /// <summary>Zone-local placement anchor for designer <c>LaidAt</c> (Zones <c>PathWalkers.Anchor</c>).</summary>
    public Vector3 PlacementAnchorLocal { get; set; } = new(float.NaN, float.NaN, float.NaN);

    public bool HasDesignerPlacementFrame =>
        !float.IsNaN(PlacementSpawnLocal.X) && !float.IsNaN(PlacementAnchorLocal.X);
    public uint FollowNpc { get; set; } = 0; // nearest Npc TemplateId to follow
    public WorldSpawnPosition Position { get; set; }
    public int RespawnTime { get; set; } = 15;
    public int DespawnTime { get; set; } = 20;

    public virtual T Spawn(uint objId)
    {
        return null;
    }

    public virtual T ForceSpawn(uint objId)
    {
        return null;
    }

    public virtual T Spawn(uint objId, ulong itemId, uint charId)
    {
        return null;
    }

    public virtual void Respawn(T obj)
    {
        Spawn(0);
    }

    public virtual void Despawn(T obj)
    {
    }
}
