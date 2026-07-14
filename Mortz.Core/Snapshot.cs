namespace Mortz.Core;

/// <summary>
/// Authoritative world state at one tick, as sent server -> clients. Wire
/// format is hand-rolled binary, quantized to keep the broadcast cheap:
/// points in 1/4 px, velocities in 1/4 px/s, both i16 (maps up to ~8k px a
/// side). LastInputSeq stays off the wire; each client gets its own ack
/// beside the packet instead of everyone's inside it. A recipient gets its
/// own complete prediction state (including PrevButtons); remote players use
/// a compact render-only record and omit simulation-only fields. Reconciling
/// from a deserialized state can miss by
/// up to 1/8 px, which the correction offset eats invisibly. Bump
/// <see cref="NetConfig.PROTOCOL_VERSION"/> on any layout change.
/// </summary>
public sealed record Snapshot(int Tick, PlayerState[] Players, MortarState[] Mortars)
{
    private const byte GROUNDED_BIT = 0x04;
    private const byte FULL_STATE_BIT = 0x80;
    private const byte SLOT_IDS_BIT = 0x80;
    private const byte ROPE_MASK = 0x03;

    /// <summary>Full records for persistence/tests. Live traffic should call
    /// <see cref="SerializeFor"/> so only the owner's prediction state is sent.</summary>
    public byte[] Serialize() => SerializeFor(null);

    public byte[] SerializeFor(int localPeerId) => SerializeFor((int?)localPeerId);

    private byte[] SerializeFor(int? localPeerId)
    {
        using MemoryStream ms = new MemoryStream();
        using BinaryWriter w = new BinaryWriter(ms);
        w.Write(Tick);
        if (Players.Length > NetConfig.MAX_PLAYERS)
            throw new InvalidDataException($"Too many players in snapshot: {Players.Length}.");
        bool slotIds = localPeerId != null;
        w.Write((byte)(Players.Length | (slotIds ? SLOT_IDS_BIT : 0)));
        foreach (PlayerState p in Players)
        {
            bool full = localPeerId == null || p.PeerId == localPeerId;
            if (slotIds)
            {
                if (p.NetSlot is 0 or > NetConfig.MAX_PLAYERS)
                    throw new InvalidDataException($"Invalid network slot {p.NetSlot} for peer {p.PeerId}.");
                w.Write(p.NetSlot);
            }
            else
                w.Write(p.PeerId);
            WriteVec(w, p.Position);
            w.Write((byte)((byte)p.Rope | (p.Grounded ? GROUNDED_BIT : 0) |
                (full ? FULL_STATE_BIT : 0)));
            if (full)
            {
                WriteVec(w, p.Velocity);
                w.Write(p.JumpsLeft);
            }
            w.Write(p.DashCooldown);
            w.Write(p.Ammo);
            w.Write(p.ReloadTicks);
            if (full)
            {
                w.Write(p.CoyoteTicks);
                w.Write(p.RopeCooldown);
            }
            w.Write(p.Aim);
            w.Write(p.Health);
            w.Write(p.RespawnTicks);
            w.Write(p.ParryTicks);
            if (full)
            {
                if (!slotIds)
                {
                    w.Write(p.Skin);
                    w.Write(p.TeamId);
                }
                w.Write(p.ParryCooldown);
                w.Write((ushort)p.PrevButtons);
            }
            if (p.Rope != RopeMode.None)
                WriteVec(w, p.RopePoint);
            if (p.Rope == RopeMode.Flying)
                WriteVec(w, p.RopeVelocity);
            if (p.Rope == RopeMode.Attached)
                w.Write(Quantize(p.RopeLength));
        }
        // OwnerId rides along so clients can hide their own shells and render
        // the predicted copies instead; SpawnSeq lets the shooter spot a shell
        // the server took over (a deflect) and retire its predicted copy.
        if (Mortars.Length > ushort.MaxValue)
            throw new InvalidDataException($"Too many mortars in snapshot: {Mortars.Length}.");
        w.Write((ushort)Mortars.Length);
        foreach (MortarState m in Mortars)
        {
            w.Write(m.Id);
            w.Write(m.OwnerId);
            w.Write(m.FiredBy);
            w.Write(m.Deflected);
            w.Write(m.SpawnSeq);
            WriteVec(w, m.Position);
            WriteVec(w, m.Velocity);
        }
        return ms.ToArray();
    }

    public static Snapshot Deserialize(byte[] data) => Deserialize(data, null);

    public static Snapshot Deserialize(byte[] data, IReadOnlyDictionary<byte, int>? peersBySlot)
    {
        using MemoryStream ms = new MemoryStream(data);
        using BinaryReader r = new BinaryReader(ms);
        int tick = r.ReadInt32();
        byte countAndFormat = r.ReadByte();
        bool slotIds = (countAndFormat & SLOT_IDS_BIT) != 0;
        int count = countAndFormat & ~SLOT_IDS_BIT;
        if (count > NetConfig.MAX_PLAYERS)
            throw new InvalidDataException($"Invalid snapshot player count {count}.");
        PlayerState[] players = new PlayerState[count];
        for (int i = 0; i < count; i++)
        {
            byte slot = slotIds ? r.ReadByte() : (byte)0;
            int peerId;
            if (slotIds)
            {
                if (peersBySlot == null || !peersBySlot.TryGetValue(slot, out peerId))
                    throw new InvalidDataException($"Unknown snapshot player slot {slot}.");
            }
            else
                peerId = r.ReadInt32();
            Vec2 position = ReadVec(r);
            byte flags = r.ReadByte();
            bool full = (flags & FULL_STATE_BIT) != 0;
            PlayerState p = new PlayerState
            {
                PeerId = peerId,
                NetSlot = slot,
                Position = position,
                Rope = (RopeMode)(flags & ROPE_MASK),
                Grounded = (flags & GROUNDED_BIT) != 0,
            };
            if (full)
            {
                p.Velocity = ReadVec(r);
                p.JumpsLeft = r.ReadByte();
            }
            p.DashCooldown = r.ReadByte();
            p.Ammo = r.ReadByte();
            p.ReloadTicks = r.ReadByte();
            if (full)
            {
                p.CoyoteTicks = r.ReadByte();
                p.RopeCooldown = r.ReadByte();
            }
            p.Aim = r.ReadByte();
            p.Health = r.ReadByte();
            p.RespawnTicks = r.ReadByte();
            p.ParryTicks = r.ReadByte();
            if (full)
            {
                if (!slotIds)
                {
                    p.Skin = r.ReadByte();
                    p.TeamId = r.ReadByte();
                }
                p.ParryCooldown = r.ReadUInt16();
                p.PrevButtons = (InputButtons)r.ReadUInt16();
            }
            if (p.Rope != RopeMode.None)
                p.RopePoint = ReadVec(r);
            if (p.Rope == RopeMode.Flying)
                p.RopeVelocity = ReadVec(r);
            if (p.Rope == RopeMode.Attached)
                p.RopeLength = r.ReadInt16() / 4f;
            players[i] = p;
        }
        int mortarCount = r.ReadUInt16();
        MortarState[] mortars = new MortarState[mortarCount];
        for (int i = 0; i < mortarCount; i++)
        {
            mortars[i] = new MortarState
            {
                Id = r.ReadUInt16(),
                OwnerId = r.ReadInt32(),
                FiredBy = r.ReadInt32(),
                Deflected = r.ReadBoolean(),
                SpawnSeq = r.ReadInt32(),
                Position = ReadVec(r),
                Velocity = ReadVec(r),
            };
        }
        if (ms.Position != ms.Length)
            throw new InvalidDataException("Trailing bytes in snapshot.");
        return new Snapshot(tick, players, mortars);
    }

    private static short Quantize(float value) =>
        (short)Math.Clamp((int)MathF.Round(value * 4f), short.MinValue, short.MaxValue);

    private static void WriteVec(BinaryWriter w, Vec2 v)
    {
        w.Write(Quantize(v.X));
        w.Write(Quantize(v.Y));
    }

    private static Vec2 ReadVec(BinaryReader r) => new(r.ReadInt16() / 4f, r.ReadInt16() / 4f);
}
