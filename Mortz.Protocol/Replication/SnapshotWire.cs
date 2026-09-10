using Mortz.Core.Replication;
using Mortz.Core.Sim;
using Mortz.Protocol.Net;
using Mortz.Protocol.Net.Roster;

namespace Mortz.Protocol.Replication;

/// <summary>
/// Hand-rolled binary layout for Snapshot, quantized to 1/4 px i16 (maps up
/// to ~8k px a side). LastInputSeq stays off the wire; each client gets its
/// own ack beside the packet. A recipient gets its own complete prediction
/// state; remote players are compact render-only records (full records for
/// everyone on the persistence path). Reconciling from a deserialized state
/// can miss by up to 1/8 px; the correction offset eats it. Bump
/// <see cref="NetConfig.PROTOCOL_VERSION"/> on any layout change.
/// </summary>
public static class SnapshotWire
{
    private const byte GROUNDED_BIT = 0x04;
    private const byte FULL_STATE_BIT = 0x80;
    private const byte SLOT_IDS_BIT = 0x80;
    private const byte ROPE_MASK = 0x03;
    public static byte[] Serialize(Snapshot snapshot, int? localPeerId) =>
        PacketEncoder.Encode(snapshot, localPeerId, Write);

    private static void Write(ref PacketWriter writer, Snapshot snapshot, int? localPeerId)
    {
        if (snapshot.Mortars.Length > ushort.MaxValue)
            throw new InvalidDataException($"Too many mortars in snapshot: {snapshot.Mortars.Length}.");
        WritePlayersHeader(ref writer, snapshot.Tick, snapshot.Players.Length, localPeerId != null);
        for (int index = 0; index < snapshot.Players.Length; index++)
        {
            WritePlayer(ref writer, snapshot.Players[index], localPeerId, (byte)(index + 1));
        }
        WriteMortars(ref writer, snapshot.Mortars);
    }

    public static void WritePlayersHeader(ref PacketWriter writer, int tick, int count, bool slotIds)
    {
        if (count is < 0 or > NetConfig.MAX_PLAYERS)
            throw new InvalidDataException($"Invalid snapshot player count {count}.");
        writer.Write(tick);
        writer.Write((byte)(count | (slotIds ? SLOT_IDS_BIT : 0)));
    }

    public static Snapshot Deserialize(byte[] data, IPeerSlots? slots = null)
    {
        using MemoryStream stream = new(data, writable: false);
        using BinaryReader reader = new(stream);
        Snapshot snapshot = Read(reader, slots);
        if (stream.Position != stream.Length)
            throw new InvalidDataException("Trailing bytes in snapshot.");
        return snapshot;
    }

    internal static Snapshot Read(BinaryReader reader, IPeerSlots? slots)
    {
        (int tick, PlayerState[] players) = ReadPlayers(reader, slots);
        MortarState[] mortars = ReadMortars(reader);
        return new Snapshot(tick, players, mortars);
    }

    internal static (int Tick, PlayerState[] Players) ReadPlayers(
        BinaryReader reader, IPeerSlots? slots)
    {
        int tick = reader.ReadInt32();
        byte countAndFormat = reader.ReadByte();
        bool slotIds = (countAndFormat & SLOT_IDS_BIT) != 0;
        int count = countAndFormat & ~SLOT_IDS_BIT;
        if (count > NetConfig.MAX_PLAYERS)
            throw new InvalidDataException($"Invalid snapshot player count {count}.");
        PlayerState[] players = new PlayerState[count];
        for (int i = 0; i < count; i++)
        {
            players[i] = ReadPlayer(reader, slotIds, slots);
        }
        return (tick, players);
    }

    public static void WritePlayer(ref PacketWriter writer, in PlayerState player,
        int? localPeerId, byte slot)
    {
        bool slotIds = localPeerId != null;
        bool full = localPeerId == null || player.PeerId == localPeerId;
        if (slotIds)
        {
            if (slot is 0 or > NetConfig.MAX_PLAYERS)
            {
                throw new InvalidDataException(
                    $"Invalid network slot {slot} for peer {player.PeerId}.");
            }

            writer.Write(slot);
        }
        else writer.Write(player.PeerId);

        WriteVec(ref writer, player.Position);
        writer.Write((byte)((byte)player.Rope | (player.Grounded ? GROUNDED_BIT : 0) |
                            (full ? FULL_STATE_BIT : 0)));
        if (full)
        {
            WriteVec(ref writer, player.Velocity);
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
                writer.Write(TeamWire.ToByte(player.Team));
            }
            writer.Write(player.SpawnImmunityFireThroughSeq);
            writer.Write(player.ParryCooldown);
            writer.Write((ushort)player.PrevButtons);
        }
        if (player.Rope != RopeMode.NONE)
            WriteVec(ref writer, player.RopePoint);
        if (player.Rope == RopeMode.FLYING)
            WriteVec(ref writer, player.RopeVelocity);
        if (player.Rope == RopeMode.ATTACHED)
            writer.Write(Quantize(player.RopeLength));
    }

    private static PlayerState ReadPlayer(BinaryReader reader, bool slotIds, IPeerSlots? slots)
    {
        byte slot = slotIds ? reader.ReadByte() : (byte)0;
        int peerId;
        if (slotIds)
        {
            if (!NetSlot.TryFrom(slot, out NetSlot netSlot))
                throw new InvalidDataException($"Invalid snapshot player slot {slot}.");
            if (slots?.PeerInSlot(netSlot) is not int resolved)
                throw new InvalidDataException($"Unknown snapshot player slot {slot}.");
            peerId = resolved;
        }
        else
            peerId = reader.ReadInt32();
        Vec2 position = ReadVec(reader);
        byte flags = reader.ReadByte();
        bool full = (flags & FULL_STATE_BIT) != 0;
        PlayerState player = new()
        {
            PeerId = peerId,
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
        player.RespawnTicks = reader.ReadUInt16();
        player.SpawnImmunityTicks = reader.ReadByte();
        player.ParryTicks = reader.ReadByte();
        if (full)
        {
            if (!slotIds)
            {
                player.Team = TeamWire.FromByte(reader.ReadByte());
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
    private static void WriteMortars(ref PacketWriter writer, MortarState[] mortars)
    {
        writer.Write((ushort)mortars.Length);
        foreach (MortarState mortar in mortars)
        {
            writer.Write(mortar.Id);
            writer.Write(mortar.OwnerId);
            writer.Write(mortar.FiredBy);
            writer.Write(mortar.Deflected);
            writer.Write(mortar.SpawnSeq);
            WriteVec(ref writer, mortar.Position);
            WriteVec(ref writer, mortar.Velocity);
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

    private static void WriteVec(ref PacketWriter writer, Vec2 value)
    {
        writer.Write(Quantize(value.X));
        writer.Write(Quantize(value.Y));
    }

    private static Vec2 ReadVec(BinaryReader reader) =>
        new(reader.ReadInt16() / 4f, reader.ReadInt16() / 4f);
}
