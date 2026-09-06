using Mortz.Core.Sim;
using Mortz.Core.Terrain;
using Mortz.Protocol.Net.Sim;

namespace Mortz.Client.Match;

public readonly record struct ImpactIdentity(int Tick, int ShellId, int OwnerId, int SpawnSeq);

public record TerrainImpact(ImpactIdentity Identity, int X, int Y, int Radius,
    IReadOnlyList<(int X, int Y)> Removed, bool PlayEffects);

public class ClientTerrain(TerrainMask mask, int radius, int localPeerId, Func<ulong> clock)
{
    private readonly CarveLedger _ledger = new();
    public TerrainMask Mask { get; } = mask;
    public event Action<TerrainImpact>? Impact;
    public event Action<IReadOnlyList<(int X, int Y)>>? Restored;

    public void Predict(int sequence, Vec2 position)
    {
        if (_ledger.IsPending(sequence) || _ledger.IsSettled(sequence))
            return;
        int x = (int)position.X, y = (int)position.Y;
        List<(int X, int Y)> removed = Mask.CarveCircle(x, y, radius);
        _ledger.AddPending(sequence, x, y, radius, removed, clock());
        Impact?.Invoke(new TerrainImpact(new ImpactIdentity(-1, -1, localPeerId, sequence),
            x, y, radius, removed, true));
    }

    public void Apply(in CarveMsg message)
    {
        ulong now = clock();
        _ledger.RecordConfirmed(message.X, message.Y, message.Radius, now);
        bool mine = message.OwnerId == localPeerId && message.SpawnSeq >= 0;
        bool predicted = false;
        if (mine)
        {
            _ledger.MarkSettled(message.SpawnSeq, now);
            if (_ledger.TryConfirm(message.SpawnSeq, out CarveLedger.PendingCarve? pending))
            {
                predicted = true;
                Restore(pending, message.X, message.Y, message.Radius);
            }
        }
        List<(int X, int Y)> removed = Mask.CarveCircle(message.X, message.Y, message.Radius);
        Impact?.Invoke(new TerrainImpact(new ImpactIdentity(message.Tick, message.ShellId,
                message.OwnerId, message.SpawnSeq), message.X, message.Y, message.Radius,
            removed, !predicted));
    }

    public bool Retire(int sequence)
    {
        _ledger.MarkSettled(sequence, clock());
        if (!_ledger.TryConfirm(sequence, out CarveLedger.PendingCarve? pending))
            return false;
        Restore(pending, 0, 0, -1);
        return true;
    }

    public void Advance()
    {
        foreach ((int _, CarveLedger.PendingCarve pending) in _ledger.Expire(clock()))
        {
            Restore(pending, 0, 0, -1);
        }
    }

    private void Restore(CarveLedger.PendingCarve pending, int x, int y, int radius)
    {
        List<(int X, int Y)> restored = [];
        foreach ((int px, int py) in pending.Removed)
        {
            if (!_ledger.ShouldRestore(px, py, x, y, radius))
                continue;
            Mask.RestoreDestructible(px, py);
            restored.Add((px, py));
        }
        if (restored.Count > 0)
            Restored?.Invoke(restored);
    }
}
