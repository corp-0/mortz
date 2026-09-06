using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Client.Audio;
using Mortz.Client.Replay;
using Mortz.Client.Replication;
using Mortz.Client.Views;
using Mortz.Core.Sim;
using Mortz.Net;
using Mortz.Shared.Logging;
using Serilog;

namespace Mortz.Client.Match;

[Meta(typeof(IAutoNode))]
public partial class MortarClient : Node
{
    private static readonly ILogger _log = MortzLog.For("client");

    [Export] private LocalPlayerController _localPlayer = null!;
    [Export] private MortarViewManager _views = null!;
    [Export] private FinalKillReplay _finalKillReplay = null!;

    [Dependency] private INetwork Network => this.DependOn<INetwork>();

    [Dependency] private ISfx Sfx => this.DependOn<ISfx>();

    [Dependency] private GameMap Map => this.DependOn<GameMap>();

    [Dependency] private ClientMatchRuntime Runtime => this.DependOn<ClientMatchRuntime>();

    public override void _Notification(int what) => this.Notify(what);

    // Successive parries climb a pentatonic scale, louder each step since
    // pitching up thins the sound.
    private static readonly float[] _parryPitches =
        Array.ConvertAll(new[] { 0, 2, 4, 7, 9, 12 }, st => Mathf.Pow(2f, st / 12f));

    private const float PARRY_GAIN_DB_PER_STEP = 1f;

    private readonly Dictionary<ushort, int> _parriesByMortar = new();
    public MortarViewManager ViewManager => _views;

    public void OnResolved() => Runtime.MortarChanged += OnMortarChanged;
    public void OnExitTree() => Runtime.MortarChanged -= OnMortarChanged;
    public IReadOnlyList<RenderMortar> SampleRemoteMortars() => Runtime.Mortars.Render();

    private void OnMortarChanged(SimWorld.MortarEvent change)
    {
        MortarState state = change.State;
        switch (change.Kind)
        {
            case SimWorld.MortarEventKind.SPAWN:
                if (state.FiredBy != Network.LocalPeerId)
                    Sfx.PlayAt(Sfx.Sounds.MortarFire, new Vector2(state.Position.X, state.Position.Y));
                break;
            case SimWorld.MortarEventKind.DEFLECT:
                PlayParrySound(state);
                break;
            case SimWorld.MortarEventKind.END:
                _parriesByMortar.Remove(state.Id);
                break;
        }
    }

    private void PlayParrySound(in MortarState state)
    {
        int step = _parriesByMortar.GetValueOrDefault(state.Id);
        _parriesByMortar[state.Id] = step + 1;
        step = Math.Min(step, _parryPitches.Length - 1);
        Sfx.PlayAt(Sfx.Sounds.ParrySuccess,
            new Vector2(state.Position.X, state.Position.Y),
            _parryPitches[step], step * PARRY_GAIN_DB_PER_STEP);
    }

}
