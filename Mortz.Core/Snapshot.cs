namespace Mortz.Core;

/// <summary>
/// Authoritative world state at one tick, as sent server → clients.
/// Wire format is hand-rolled binary; bump <see cref="NetConfig.PROTOCOL_VERSION"/>
/// on any layout change.
/// </summary>
public sealed record Snapshot(int Tick, PlayerState[] Players)
{
    public byte[] Serialize()
    {
        using MemoryStream ms = new MemoryStream();
        using BinaryWriter w = new BinaryWriter(ms);
        w.Write(Tick);
        w.Write((byte)Players.Length);
        foreach (PlayerState p in Players)
        {
            w.Write(p.PeerId);
            w.Write(p.Position.X);
            w.Write(p.Position.Y);
            w.Write(p.Velocity.X);
            w.Write(p.Velocity.Y);
            w.Write(p.Grounded);
            w.Write(p.LastInputSeq);
            w.Write(p.JumpsLeft);
            w.Write(p.DashCooldown);
            w.Write(p.CoyoteTicks);
            w.Write(p.RopeCooldown);
            w.Write((byte)p.Rope);
            if (p.Rope != RopeMode.None)
            {
                w.Write(p.RopePoint.X);
                w.Write(p.RopePoint.Y);
            }
            if (p.Rope == RopeMode.Flying)
            {
                w.Write(p.RopeVelocity.X);
                w.Write(p.RopeVelocity.Y);
            }
            if (p.Rope == RopeMode.Attached)
                w.Write(p.RopeLength);
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
            PlayerState p = new PlayerState
            {
                PeerId = r.ReadInt32(),
                Position = new Vec2(r.ReadSingle(), r.ReadSingle()),
                Velocity = new Vec2(r.ReadSingle(), r.ReadSingle()),
                Grounded = r.ReadBoolean(),
                LastInputSeq = r.ReadInt32(),
                JumpsLeft = r.ReadByte(),
                DashCooldown = r.ReadByte(),
                CoyoteTicks = r.ReadByte(),
                RopeCooldown = r.ReadByte(),
                Rope = (RopeMode)r.ReadByte(),
            };
            if (p.Rope != RopeMode.None)
                p.RopePoint = new Vec2(r.ReadSingle(), r.ReadSingle());
            if (p.Rope == RopeMode.Flying)
                p.RopeVelocity = new Vec2(r.ReadSingle(), r.ReadSingle());
            if (p.Rope == RopeMode.Attached)
                p.RopeLength = r.ReadSingle();
            players[i] = p;
        }
        return new Snapshot(tick, players);
    }
}
