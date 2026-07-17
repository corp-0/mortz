using Mortz.Core.Net;
using Mortz.Core.Sim;

namespace Mortz.Core.Replication;

/// <summary>
/// Hand-rolled binary layout for Snapshot, quantized to keep the broadcast
/// cheap: points in 1/4 px, velocities in 1/4 px/s, both i16 (maps up to ~8k
/// px a side). LastInputSeq stays off the wire; each client gets its own ack
/// beside the packet instead of everyone's inside it. A recipient gets its own
/// complete prediction state (including PrevButtons); remote players use a
/// compact render-only record and omit simulation-only fields. Reconciling from
/// a deserialized state can miss by up to 1/8 px, which the correction offset
/// eats invisibly. Full records for everyone on the persistence path. Bump
/// <see cref="NetConfig.PROTOCOL_VERSION"/> on any layout change.
/// </summary>
internal static class SnapshotWire
{
    private const byte GROUNDED_BIT = 0x04;
    private const byte FULL_STATE_BIT = 0x80;
    private const byte SLOT_IDS_BIT = 0x80;
    private const byte ROPE_MASK = 0x03;

    public static byte[] Serialize(Snapshot snapshot, int? localPeerId)
    {
        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream);
        writer.Write(snapshot.Tick);
        if (snapshot.Players.Length > NetConfig.MAX_PLAYERS)
            throw new InvalidDataException($"Too many players in snapshot: {snapshot.Players.Length}.");
        bool slotIds = localPeerId != null;
        writer.Write((byte)(snapshot.Players.Length | (slotIds ? SLOT_IDS_BIT : 0)));
        foreach (PlayerState player in snapshot.Players)
            WritePlayer(writer, player, localPeerId, slotIds);
        WriteMortars(writer, snapshot.Mortars);
        return stream.ToArray();
    }

    public static Snapshot Deserialize(byte[] data, IReadOnlyDictionary<byte, int>? peersBySlot)
    {
        using MemoryStream stream = new(data, writable: false);
        using BinaryReader reader = new(stream);
        int tick = reader.ReadInt32();
        byte countAndFormat = reader.ReadByte();
        bool slotIds = (countAndFormat & SLOT_IDS_BIT) != 0;
        int count = countAndFormat & ~SLOT_IDS_BIT;
        if (count > NetConfig.MAX_PLAYERS)
            throw new InvalidDataException($"Invalid snapshot player count {count}.");
        PlayerState[] players = new PlayerState[count];
        for (int i = 0; i < count; i++)
            players[i] = ReadPlayer(reader, slotIds, peersBySlot);
        MortarState[] mortars = ReadMortars(reader);
        if (stream.Position != stream.Length)
            throw new InvalidDataException("Trailing bytes in snapshot.");
        return new Snapshot(tick, players, mortars);
    }

    private static void WritePlayer(BinaryWriter writer, in PlayerState player,
        int? localPeerId, bool slotIds)
    {
        bool full = localPeerId == null || player.PeerId == localPeerId;
        if (slotIds)
        {
            if (player.NetSlot is 0 or > NetConfig.MAX_PLAYERS)
                throw new InvalidDataException(
                    $"Invalid network slot {player.NetSlot} for peer {player.PeerId}.");
            writer.Write(player.NetSlot);
        }
        else
            writer.Write(player.PeerId);
        WriteVec(writer, player.Position);
        writer.Write((byte)((byte)player.Rope | (player.Grounded ? GROUNDED_BIT : 0) |
                            (full ? FULL_STATE_BIT : 0)));
        if (full)
        {
            WriteVec(writer, player.Velocity);
            writer.Write(player.JumpsLeft);
        }
        writer.Write(player.DashCooldown);
        writer.Write(player.Ammo);
        writer.Write(player.ReloadTicks);
        if (full)
        {
            writer.Write(player.CoyoteTicks);
            writer.Write(player.RopeCooldown);
        }
        writer.Write(player.Aim);
        writer.Write(player.Health);
        writer.Write(player.RespawnTicks);
        writer.Write(player.SpawnImmunityTicks);
        writer.Write(player.ParryTicks);
        if (full)
        {
            if (!slotIds)
            {
                writer.Write(player.Skin);
                writer.Write(player.TeamId);
            }
            writer.Write(player.SpawnImmunityFireThroughSeq);
            writer.Write(player.ParryCooldown);
            writer.Write((ushort)player.PrevButtons);
        }
        if (player.Rope != RopeMode.NONE)
            WriteVec(writer, player.RopePoint);
        if (player.Rope == RopeMode.FLYING)
            WriteVec(writer, player.RopeVelocity);
        if (player.Rope == RopeMode.ATTACHED)
            writer.Write(Quantize(player.RopeLength));
    }

    private static PlayerState ReadPlayer(BinaryReader reader, bool slotIds,
        IReadOnlyDictionary<byte, int>? peersBySlot)
    {
        byte slot = slotIds ? reader.ReadByte() : (byte)0;
        int peerId;
        if (slotIds)
        {
            if (peersBySlot == null || !peersBySlot.TryGetValue(slot, out peerId))
                throw new InvalidDataException($"Unknown snapshot player slot {slot}.");
        }
        else
            peerId = reader.ReadInt32();
        Vec2 position = ReadVec(reader);
        byte flags = reader.ReadByte();
        bool full = (flags & FULL_STATE_BIT) != 0;
        PlayerState player = new()
        {
            PeerId = peerId,
            NetSlot = slot,
            Position = position,
            Rope = (RopeMode)(flags & ROPE_MASK),
            Grounded = (flags & GROUNDED_BIT) != 0,
        };
        if (full)
        {
            player.Velocity = ReadVec(reader);
            player.JumpsLeft = reader.ReadByte();
        }
        player.DashCooldown = reader.ReadByte();
        player.Ammo = reader.ReadByte();
        player.ReloadTicks = reader.ReadByte();
        if (full)
        {
            player.CoyoteTicks = reader.ReadByte();
            player.RopeCooldown = reader.ReadByte();
        }
        player.Aim = reader.ReadByte();
        player.Health = reader.ReadByte();
        player.RespawnTicks = reader.ReadByte();
        player.SpawnImmunityTicks = reader.ReadByte();
        player.ParryTicks = reader.ReadByte();
        if (full)
        {
            if (!slotIds)
            {
                player.Skin = reader.ReadByte();
                player.TeamId = reader.ReadByte();
            }
            player.SpawnImmunityFireThroughSeq = reader.ReadInt32();
            player.ParryCooldown = reader.ReadUInt16();
            player.PrevButtons = (InputButtons)reader.ReadUInt16();
        }
        if (player.Rope != RopeMode.NONE)
            player.RopePoint = ReadVec(reader);
        if (player.Rope == RopeMode.FLYING)
            player.RopeVelocity = ReadVec(reader);
        if (player.Rope == RopeMode.ATTACHED)
            player.RopeLength = reader.ReadInt16() / 4f;
        return player;
    }

    // OwnerId rides along so clients can hide their own shells and render the
    // predicted copies instead; SpawnSeq lets the shooter spot a shell the
    // server took over (a deflect) and retire its predicted copy.
    private static void WriteMortars(BinaryWriter writer, MortarState[] mortars)
    {
        if (mortars.Length > ushort.MaxValue)
            throw new InvalidDataException($"Too many mortars in snapshot: {mortars.Length}.");
        writer.Write((ushort)mortars.Length);
        foreach (MortarState mortar in mortars)
        {
            writer.Write(mortar.Id);
            writer.Write(mortar.OwnerId);
            writer.Write(mortar.FiredBy);
            writer.Write(mortar.Deflected);
            writer.Write(mortar.SpawnSeq);
            WriteVec(writer, mortar.Position);
            WriteVec(writer, mortar.Velocity);
        }
    }

    private static MortarState[] ReadMortars(BinaryReader reader)
    {
        int count = reader.ReadUInt16();
        MortarState[] mortars = new MortarState[count];
        for (int i = 0; i < count; i++)
        {
            mortars[i] = new MortarState
            {
                Id = reader.ReadUInt16(),
                OwnerId = reader.ReadInt32(),
                FiredBy = reader.ReadInt32(),
                Deflected = reader.ReadBoolean(),
                SpawnSeq = reader.ReadInt32(),
                Position = ReadVec(reader),
                Velocity = ReadVec(reader),
            };
        }
        return mortars;
    }

    private static short Quantize(float value) =>
        (short)Math.Clamp((int)MathF.Round(value * 4f), short.MinValue, short.MaxValue);

    private static void WriteVec(BinaryWriter writer, Vec2 value)
    {
        writer.Write(Quantize(value.X));
        writer.Write(Quantize(value.Y));
    }

    private static Vec2 ReadVec(BinaryReader reader) =>
        new(reader.ReadInt16() / 4f, reader.ReadInt16() / 4f);
}
