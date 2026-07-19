namespace Mortz.Core.Sim.Modifiers;

/// <summary>Binary layout for one player's modifier list as it rides inside
/// PlayerModifiersMsg. The list order is preserved on the wire; the server
/// sends it canonically sorted and the client composes it as received.</summary>
public static class ModifierWire
{
    public static byte[] Serialize(IReadOnlyList<StatsModifier> modifiers)
    {
        using MemoryStream stream = new();
        using BinaryWriter w = new(stream);
        w.Write((byte)modifiers.Count);
        foreach (StatsModifier modifier in modifiers)
        {
            w.Write((byte)modifier.Id);
            w.Write((byte)modifier.Changes.Count);
            foreach (StatChange change in modifier.Changes)
            {
                w.Write((byte)change.Stat);
                w.Write((byte)change.Op);
                w.Write(change.Value);
            }
        }
        return stream.ToArray();
    }

    public static List<StatsModifier> Deserialize(byte[] data)
    {
        using MemoryStream stream = new(data, writable: false);
        using BinaryReader r = new(stream);
        int count = r.ReadByte();
        List<StatsModifier> modifiers = new(count);
        for (int i = 0; i < count; i++)
        {
            ModifierId id = (ModifierId)r.ReadByte();
            int changeCount = r.ReadByte();
            StatChange[] changes = new StatChange[changeCount];
            for (int j = 0; j < changeCount; j++)
            {
                Stat stat = (Stat)r.ReadByte();
                StatOp op = (StatOp)r.ReadByte();
                if (!Enum.IsDefined(stat) || !Enum.IsDefined(op))
                    throw new InvalidDataException("Unknown stat or op in modifier list.");
                changes[j] = new StatChange(stat, op, r.ReadSingle());
            }
            modifiers.Add(new StatsModifier(id, changes));
        }
        if (stream.Position != stream.Length)
            throw new InvalidDataException("Trailing bytes in modifier list.");
        return modifiers;
    }
}
