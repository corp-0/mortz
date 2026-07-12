namespace Mortz.Core;

/// <summary>
/// Authoritative world state at one tick, as sent server -> clients. Wire
/// format is hand-rolled binary, quantized to keep the broadcast cheap:
/// points in 1/4 px, velocities in 1/4 px/s, both i16 (maps up to ~8k px a
/// side). LastInputSeq stays off the wire; each client gets its own ack
/// beside the packet instead of everyone's inside it. Reconciling from a
/// deserialized state can therefore miss by up to 1/8 px, which the
/// correction offset eats invisibly. Bump <see cref="NetConfig.PROTOCOL_VERSION"/>
/// on any layout change.
/// </summary>
public sealed record Snapshot(int Tick, PlayerState[] Players, MortarState[] Mortars)
{
    private const byte GROUNDED_BIT = 0x04;
    private const byte ROPE_MASK = 0x03;

    public byte[] Serialize()
    {
        using MemoryStream ms = new MemoryStream();
        using BinaryWriter w = new BinaryWriter(ms);
        w.Write(Tick);
        w.Write((byte)Players.Length);
        foreach (PlayerState p in Players)
        {
            w.Write(p.PeerId);
            WriteVec(w, p.Position);
            WriteVec(w, p.Velocity);
            w.Write((byte)((byte)p.Rope | (p.Grounded ? GROUNDED_BIT : 0)));
            w.Write(p.JumpsLeft);
            w.Write(p.DashCooldown);
            w.Write(p.Ammo);
            w.Write(p.ReloadTicks);
            w.Write(p.CoyoteTicks);
            w.Write(p.RopeCooldown);
            w.Write(p.Aim);
            w.Write(p.Skin);
            if (p.Rope != RopeMode.None)
                WriteVec(w, p.RopePoint);
            if (p.Rope == RopeMode.Flying)
                WriteVec(w, p.RopeVelocity);
            if (p.Rope == RopeMode.Attached)
                w.Write(Quantize(p.RopeLength));
        }
        // OwnerId rides along so clients can hide their own shells and render
        // the predicted copies instead.
        w.Write((byte)Mortars.Length);
        foreach (MortarState m in Mortars)
        {
            w.Write(m.Id);
            w.Write(m.OwnerId);
            WriteVec(w, m.Position);
            WriteVec(w, m.Velocity);
        }
        return ms.ToArray();
    }

    public static Snapshot Deserialize(byte[] data)
    {
        using MemoryStream ms = new MemoryStream(data);
        using BinaryReader r = new BinaryReader(ms);
        int tick = r.ReadInt32();
        int count = r.ReadByte();
        PlayerState[] players = new PlayerState[count];
        for (int i = 0; i < count; i++)
        {
            int peerId = r.ReadInt32();
            Vec2 position = ReadVec(r);
            Vec2 velocity = ReadVec(r);
            byte flags = r.ReadByte();
            PlayerState p = new PlayerState
            {
                PeerId = peerId,
                Position = position,
                Velocity = velocity,
                Rope = (RopeMode)(flags & ROPE_MASK),
                Grounded = (flags & GROUNDED_BIT) != 0,
                JumpsLeft = r.ReadByte(),
                DashCooldown = r.ReadByte(),
                Ammo = r.ReadByte(),
                ReloadTicks = r.ReadByte(),
                CoyoteTicks = r.ReadByte(),
                RopeCooldown = r.ReadByte(),
                Aim = r.ReadByte(),
                Skin = r.ReadByte(),
            };
            if (p.Rope != RopeMode.None)
                p.RopePoint = ReadVec(r);
            if (p.Rope == RopeMode.Flying)
                p.RopeVelocity = ReadVec(r);
            if (p.Rope == RopeMode.Attached)
                p.RopeLength = r.ReadInt16() / 4f;
            players[i] = p;
        }
        int mortarCount = r.ReadByte();
        MortarState[] mortars = new MortarState[mortarCount];
        for (int i = 0; i < mortarCount; i++)
        {
            mortars[i] = new MortarState
            {
                Id = r.ReadUInt16(),
                OwnerId = r.ReadInt32(),
                Position = ReadVec(r),
                Velocity = ReadVec(r),
            };
        }
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

