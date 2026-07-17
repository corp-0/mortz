using Mortz.Core.Sim;

namespace Mortz.Core.Replication;

/// <summary>Compact wire encoding for shell corrections and lifecycle events.</summary>
public static class MortarWire
{
    public const int CORRECTION_BYTES_PER_SHELL = 10;
    private const int MAX_LIFECYCLE_EVENTS = 2_048;
    public const int LIFECYCLE_EVENTS_PER_BATCH = 256;

    public static short Quantize(float value) =>
        (short)Math.Clamp((int)MathF.Round(value * 4f), short.MinValue, short.MaxValue);

    public static float Dequantize(short value) => value / 4f;

    public static byte[] SerializeCorrections(IReadOnlyList<MortarState> mortars)
    {
        if (mortars.Count > SimConfig.MAX_ACTIVE_MORTARS)
            throw new InvalidDataException($"Too many mortar corrections: {mortars.Count}.");
        using MemoryStream ms = new(2 + mortars.Count * CORRECTION_BYTES_PER_SHELL);
        using BinaryWriter w = new(ms);
        w.Write((ushort)mortars.Count);
        foreach (MortarState m in mortars)
        {
            w.Write(m.Id);
            w.Write(Quantize(m.Position.X));
            w.Write(Quantize(m.Position.Y));
            w.Write(Quantize(m.Velocity.X));
            w.Write(Quantize(m.Velocity.Y));
        }
        return ms.ToArray();
    }

    public static byte[] SerializeLifecycle(int tick, IReadOnlyList<SimWorld.MortarEvent> events)
    {
        if (events.Count > MAX_LIFECYCLE_EVENTS)
            throw new InvalidDataException($"Too many mortar lifecycle events: {events.Count}.");
        return SerializeLifecycleRange(tick, events, 0, events.Count);
    }

    /// <summary>Bounded send-side batches. Lifecycle events are reliable, so
    /// an oversized burst must be split rather than dropped or allowed to
    /// throw from the server physics loop.</summary>
    public static IReadOnlyList<byte[]> SerializeLifecycleBatches(
        int tick, IReadOnlyList<SimWorld.MortarEvent> events)
    {
        if (events.Count == 0)
            return [];
        List<byte[]> batches = new((events.Count + LIFECYCLE_EVENTS_PER_BATCH - 1) /
                                   LIFECYCLE_EVENTS_PER_BATCH);
        for (int offset = 0; offset < events.Count; offset += LIFECYCLE_EVENTS_PER_BATCH)
        {
            int count = Math.Min(LIFECYCLE_EVENTS_PER_BATCH, events.Count - offset);
            batches.Add(SerializeLifecycleRange(tick, events, offset, count));
        }
        return batches;
    }

    private static byte[] SerializeLifecycleRange(int tick,
        IReadOnlyList<SimWorld.MortarEvent> events, int offset, int count)
    {
        using MemoryStream ms = new();
        using BinaryWriter w = new(ms);
        w.Write(tick);
        w.Write((ushort)count);
        for (int i = offset; i < offset + count; i++)
        {
            SimWorld.MortarEvent e = events[i];
            MortarState m = e.State;
            w.Write((byte)e.Kind);
            w.Write(m.Id);
            if (e.Kind == SimWorld.MortarEventKind.END)
                continue;
            w.Write(m.OwnerId);
            w.Write(m.FiredBy);
            w.Write(m.SpawnSeq);
            w.Write(m.Deflected);
            w.Write(Quantize(m.Position.X));
            w.Write(Quantize(m.Position.Y));
            w.Write(Quantize(m.Velocity.X));
            w.Write(Quantize(m.Velocity.Y));
        }
        return ms.ToArray();
    }

    public static bool TryReadLifecycle(byte[] data, out int tick,
        out List<SimWorld.MortarEvent> events)
    {
        tick = 0;
        events = [];
        try
        {
            using MemoryStream ms = new(data, writable: false);
            using BinaryReader r = new(ms);
            tick = r.ReadInt32();
            int count = r.ReadUInt16();
            if (count > MAX_LIFECYCLE_EVENTS)
                return false;
            events = new List<SimWorld.MortarEvent>(count);
            for (int i = 0; i < count; i++)
            {
                SimWorld.MortarEventKind kind = (SimWorld.MortarEventKind)r.ReadByte();
                if (kind > SimWorld.MortarEventKind.END)
                    return false;
                MortarState state = new() { Id = r.ReadUInt16() };
                if (kind != SimWorld.MortarEventKind.END)
                {
                    state.OwnerId = r.ReadInt32();
                    state.FiredBy = r.ReadInt32();
                    state.SpawnSeq = r.ReadInt32();
                    state.Deflected = r.ReadBoolean();
                    state.Position = ReadVec(r);
                    state.Velocity = ReadVec(r);
                }
                events.Add(new SimWorld.MortarEvent(kind, state));
            }
            return ms.Position == ms.Length;
        }
        catch (EndOfStreamException)
        {
            tick = 0;
            events = [];
            return false;
        }
    }

    public static bool TryReadCorrections(byte[] data, out List<(ushort Id, Vec2 Position, Vec2 Velocity)> states)
    {
        states = [];
        try
        {
            using MemoryStream ms = new(data, writable: false);
            using BinaryReader r = new(ms);
            int count = r.ReadUInt16();
            if (count > SimConfig.MAX_ACTIVE_MORTARS ||
                data.Length != 2 + count * CORRECTION_BYTES_PER_SHELL)
                return false;
            states = new List<(ushort, Vec2, Vec2)>(count);
            for (int i = 0; i < count; i++)
            {
                states.Add((r.ReadUInt16(), ReadVec(r), ReadVec(r)));
            }
            return true;
        }
        catch (EndOfStreamException)
        {
            states = [];
            return false;
        }
    }

    private static Vec2 ReadVec(BinaryReader r) =>
        new(Dequantize(r.ReadInt16()), Dequantize(r.ReadInt16()));
}

