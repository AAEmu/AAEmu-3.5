using System.Text.RegularExpressions;
using System.Collections.Concurrent;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Skills.Buffs;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Static;
using AAEmu.Game.Models.Game.World;

using NLog;

namespace AAEmu.Game.Core.Managers;

public class MateManager(WorldInstance parentWorldInstance)
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private Regex _nameRegex;

    private readonly ConcurrentDictionary<uint, List<Mate>> _activeMates = []; // ownerId, mount snapshot

    private WorldInstance World { get; init; } = parentWorldInstance;

    /// <summary>
    /// Gets active pets
    /// </summary>
    /// <param name="ownerId"></param>
    /// <returns></returns>
    public List<Mate> GetActiveMates(uint ownerId)
    {
        return _activeMates.GetValueOrDefault(ownerId) ?? [];
    }

    /// <summary>
    /// Gets an active pet by it's TlId
    /// </summary>
    /// <param name="tlId"></param>
    /// <returns></returns>
    public Mate GetActiveMateByTlId(uint tlId)
    {
        return _activeMates.Values.SelectMany(mateList => mateList).FirstOrDefault(mate => mate.TlId == tlId);
    }

    internal Mate GetActiveOwnedMateByTlId(uint ownerId, uint tlId) =>
        _activeMates.TryGetValue(ownerId, out var mates) ? FindMateByTlId(mates, tlId) : null;

    private static Mate FindMateByTlId(List<Mate> mates, uint tlId)
    {
        foreach (var mate in mates)
            if (mate.TlId == tlId)
                return mate;

        return null;
    }

    /// <summary>
    /// Gets an active pet by it's ObjId
    /// </summary>
    /// <param name="mateObjId"></param>
    /// <returns></returns>
    public Mate GetActiveMateByMateObjId(uint mateObjId)
    {
        return _activeMates.Values.SelectMany(mateList => mateList).FirstOrDefault(mate => mate.ObjId == mateObjId);
    }

    /// <summary>
    /// Checks if a ObjId is mounted on any of the pets and returns which seat
    /// </summary>
    /// <param name="objId"></param>
    /// <param name="attachPoint"></param>
    /// <returns></returns>
    public Mate GetIsMounted(uint objId, out AttachPointKind attachPoint)
    {
        attachPoint = AttachPointKind.System;
        foreach (var mate in _activeMates.Values.SelectMany(mateList => mateList))
            foreach (var ati in mate.Passengers.Where(ati => ati.Value._objId == objId))
            {
                attachPoint = ati.Key;
                return mate;
            }

        return null;
    }

    /// <summary>
    /// Change the state of a pet
    /// </summary>
    /// <param name="connection"></param>
    /// <param name="tlId"></param>
    /// <param name="newState"></param>
    public void ChangeStateMate(GameConnection connection, uint tlId, byte newState)
    {
        var owner = connection.ActiveChar;
        var mateInfoList = GetActiveMates(owner.Id);
        foreach (var mateInfo in mateInfoList)
        {
            if (mateInfo?.TlId != tlId) continue;
            mateInfo.UserState = newState; // TODO - Maybe verify range
            // owner.BroadcastPacket(new SCMateStatePacket(mateInfo.ObjId), true);
        }
    }

    /// <summary>
    /// Changes the current target of a pet 
    /// </summary>
    /// <param name="connection"></param>
    /// <param name="tlId"></param>
    /// <param name="objId"></param>
    public void ChangeTargetMate(GameConnection connection, uint tlId, uint objId)
    {
        // var owner = connection.ActiveChar;
        var mateInfo = GetActiveMateByTlId(tlId);
        if (mateInfo == null) return;
        mateInfo.CurrentTarget = objId > 0 ? World.GetUnit(objId) : null;
        mateInfo.BroadcastPacket(new SCTargetChangedPacket(mateInfo.ObjId, mateInfo.CurrentTarget?.ObjId ?? 0), true);

        Logger.Debug($"ChangeTargetMate. tlId: {mateInfo.TlId}, objId: {mateInfo.ObjId}, targetObjId: {objId}");
    }

    /// <summary>
    /// Renames a pet
    /// </summary>
    /// <param name="connection"></param>
    /// <param name="tlId"></param>
    /// <param name="newName"></param>
    /// <returns></returns>
    public Mate RenameMount(GameConnection connection, uint tlId, string newName)
    {
        var mateInfo = GetActiveMateByTlId(tlId);
        if (mateInfo == null || mateInfo.OwnerObjId != connection.ActiveChar.ObjId)
            return null;
        if (string.IsNullOrWhiteSpace(newName) || newName.Length == 0 || !_nameRegex.IsMatch(newName)) return null;
        mateInfo.Name = newName.NormalizeName();
        mateInfo.BroadcastPacket(new SCUnitNameChangedPacket(mateInfo.ObjId, newName), false);
        return mateInfo;
    }

    /// <summary>
    /// Mounts the active character of a connection on target mount by its TlId
    /// </summary>
    /// <param name="connection"></param>
    /// <param name="tlId"></param>
    /// <param name="attachPoint"></param>
    /// <param name="reason"></param>
    public void MountMate(GameConnection connection, uint tlId, AttachPointKind attachPoint, AttachUnitReason reason)
    {
        var character = connection.ActiveChar;
        var mateInfo = GetActiveMateByTlId(tlId);
        if (mateInfo == null) return;

        // Request seat position
        if (mateInfo.Passengers.TryGetValue(attachPoint, out var seatInfo))
        {
            // If first seat, check if it's the owner
            if (attachPoint == AttachPointKind.Driver && mateInfo.OwnerObjId != character.ObjId)
            {
                Logger.Warn($"MountMate. Non-owner {character.Name} ({character.ObjId}) tried to take the first seat on mount {mateInfo.Name} ({mateInfo.ObjId})");
                return;
            }

            // Check if seat is empty
            if (seatInfo._objId == 0)
            {
                character.BroadcastPacket(new SCUnitAttachedPacket(character.ObjId, attachPoint, reason, mateInfo.ObjId), true);
                seatInfo._objId = character.ObjId;
                seatInfo._reason = reason;

                character.Transform.Parent = mateInfo.Transform;
                character.Transform.Local.SetPosition(0, 0, 0); // correct the position of the character
                character.IsRiding = true;
                character.AttachedPoint = attachPoint;

                character.IsVisible = true; // When we're on a horse, you can see us
            }
        }
        else
        {
            Logger.Warn($"MountMate. Player {character.Name} ({character.ObjId}) tried to take a invalid seat {attachPoint} on mount {mateInfo.Name} ({mateInfo.ObjId})");
            return;
        }

        character.Buffs.TriggerRemoveOn(BuffRemoveOn.Mount);
        Logger.Debug($"MountMate. mountTlId: {mateInfo.TlId}, attachPoint: {attachPoint}, reason: {reason}, seats: {string.Join(", ", mateInfo.Passengers.Values.ToList())}");
    }

    /// <summary>
    /// Unmounts a character from target mount using its TlId
    /// </summary>
    /// <param name="character"></param>
    /// <param name="tlId"></param>
    /// <param name="attachPoint"></param>
    /// <param name="reason"></param>
    public void UnMountMate(Character character, uint tlId, AttachPointKind attachPoint, AttachUnitReason reason)
    {
        var mateInfo = GetActiveMateByTlId(tlId);
        if (mateInfo != null)
            UnMountMate(character, mateInfo, attachPoint, reason);
    }

    private void UnMountMate(Character character, Mate mateInfo, AttachPointKind attachPoint, AttachUnitReason reason)
    {
        mateInfo.StopUpdateXp();

        // Request seat position
        Character targetObj = null;
        if (mateInfo.Passengers.TryGetValue(attachPoint, out var seatInfo))
        {
            // Check if seat is taken by player
            if (seatInfo._objId != 0)
            {
                targetObj = WorldManager.Instance.GetCharacterByObjId(seatInfo._objId);
                seatInfo._objId = 0;
                seatInfo._reason = reason;
            }
        }

        if (targetObj != null)
        {
            //targetObj.Transform.StickyParent = null;
            targetObj.Transform.Parent = null;
            targetObj.SetPosition(mateInfo.Transform.World.Position.X, mateInfo.Transform.World.Position.Y, mateInfo.Transform.World.Position.Z,
                mateInfo.Transform.World.Rotation.X, mateInfo.Transform.World.Rotation.Y, mateInfo.Transform.World.Rotation.Z);
            // character.Transform = mateInfo.Transform.CloneDetached(character);
            targetObj.IsRiding = false;
            targetObj.AttachedPoint = AttachPointKind.None;

            targetObj.BroadcastPacket(new SCUnitDetachedPacket(targetObj.ObjId, reason), true);

            targetObj.Events.OnUnmount(character, new OnUnmountArgs());

            mateInfo.Buffs.TriggerRemoveOn(BuffRemoveOn.Unmount);
            targetObj.Buffs.TriggerRemoveOn(BuffRemoveOn.Unmount);
            Logger.Debug($"UnMountMate. mountTlId: {mateInfo.TlId}, targetObjId: {targetObj.ObjId}, attachPoint: {attachPoint}, reason: {reason}");
        }
        else
        {
            Logger.Debug($"UnMountMate. No valid seat entry, mountTlId: {mateInfo.TlId}, characterObjId: {0}, attachPoint: {attachPoint}, reason: {reason}");
        }
    }

    /// <summary>
    /// Adds a new pet (or despawns the previous one)
    /// </summary>
    /// <param name="owner"></param>
    /// <param name="mate"></param>
    /// <param name="item"></param>
    public void AddActiveMateAndSpawn(Character owner, Mate mate, Item item)
    {
        // The 3.5 client only sends a mount request at distances strictly below 3 m.
        mate.Transform.Local.AddDistanceToFront(mate.MateType == MateType.Ride ? 2.5f : 3f);

        // TODO: For later versions, allow multiple pets if they are different types (mount, battle, skill)
        if (!_activeMates.TryAdd(owner.Id, [mate]))
        {
            foreach (var mateInfo in GetActiveMates(owner.Id))
                owner.Mates.DespawnMate(mateInfo.TlId);
            return;
        }

        owner.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.UpdateSummonMateItem, [new ItemUpdate(item)], [])); // TODO - maybe update details
        owner.SendPacket(new SCMateSpawnedPacket(mate));
        Thread.Sleep(50);
        mate.Spawn();

        Logger.Debug($"Mount spawned. ownerObjId: {owner.ObjId}, tlId: {mate.TlId}, mateObjId: {mate.ObjId}");
    }

    /// <summary>
    /// Despawns and Removes a pet
    /// </summary>
    /// <param name="owner"></param>
    /// <param name="tlId"></param>
    public void RemoveActiveMateAndDespawn(Character owner, uint tlId)
    {
        if (!_activeMates.TryGetValue(owner.Id, out var activeMateList))
            return; // skip if invalid tlId

        var mateInfo = FindMateByTlId(activeMateList, tlId);
        if (mateInfo == null || !_activeMates.TryRemove(new KeyValuePair<uint, List<Mate>>(owner.Id, activeMateList)))
            return;

        mateInfo.StopUpdateXp();
        foreach (var ati in mateInfo.Passengers)
        {
            if (ati.Value._objId == 0)
                continue;
            UnMountMate(WorldManager.Instance.GetCharacterByObjId(ati.Value._objId), mateInfo, ati.Key, AttachUnitReason.SlaveBinding);
        }
        
        mateInfo.Delete();

        ObjectIdManager.Instance.ReleaseId(mateInfo.ObjId);
        TlIdManager.Instance.ReleaseId(mateInfo.TlId);

        Logger.Debug($"Mount removed. ownerObjId: {owner.ObjId}, tlId: {mateInfo.TlId}, mateObjId: {mateInfo.ObjId}");
    }

    /// <summary>
    /// Remove all mounts that are in the world and owned by character
    /// </summary>
    /// <param name="character"></param>
    public void RemoveAndDespawnAllActiveOwnedMates(Character character)
    {
        if (character?.Mates == null)
            return;
        foreach (var mate in GetActiveMates(character.Id))
            character.Mates.DespawnMate(this, mate.TlId);
    }

    /// <summary>
    /// Load pet related data from DB
    /// </summary>
    public void Load()
    {
        _nameRegex = new Regex(AppConfiguration.Instance.CharacterNameRegex, RegexOptions.Compiled);
        _activeMates.Clear();
    }
}
