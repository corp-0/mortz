namespace Mortz.Core.Features;

/// <summary>Owns feature registration, subscriptions, and state for one lifetime.</summary>
public class FeatureScope : IDisposable
{
    private readonly List<object> _features = [];
    private readonly List<object> _attached = [];
    private readonly List<Action> _cleanup = [];
    private readonly List<Func<string>> _stateDescriptions = [];
    private readonly Dictionary<Type, object> _contracts = [];
    private readonly CancellationTokenSource _lifetime = new();
    private Action<object>? _attach;
    private Action<object>? _detach;
    private bool _started;
    private bool _closed;

    public bool IsClosed => _closed;
    public CancellationToken Lifetime { get; }

    public FeatureScope(Action<object>? attach = null, Action<object>? detach = null)
    {
        _attach = attach;
        _detach = detach;
        Lifetime = _lifetime.Token;
    }

    public void Bind(Action<object> attach, Action<object> detach)
    {
        ObjectDisposedException.ThrowIf(_closed, this);
        if (_attach != null)
            throw new InvalidOperationException("The scope is already bound.");
        _attach = attach;
        _detach = detach;
        if (_started)
            Attach();
    }

    public T Register<T>(T feature) where T : class
    {
        ObjectDisposedException.ThrowIf(_closed, this);
        if (_started)
            throw new InvalidOperationException("Feature registration is closed.");
        if (_features.Any(existing => ReferenceEquals(existing, feature)))
            throw new InvalidOperationException("The feature is already registered.");
        _features.Add(feature);
        _contracts.Clear();
        return feature;
    }

    public void Own(Action cleanup)
    {
        ObjectDisposedException.ThrowIf(_closed, this);
        _cleanup.Add(cleanup);
    }

    public void OwnState(Action retire, Func<string> describe)
    {
        Own(retire);
        _stateDescriptions.Add(describe);
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_closed, this);
        if (_started)
            throw new InvalidOperationException("The scope has already started.");
        _started = true;
        Attach();
    }

    private void Attach()
    {
        if (_attach == null)
            return;
        try
        {
            foreach (object feature in _features)
            {
                _attach(feature);
                _attached.Add(feature);
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public IReadOnlyList<T> Implementing<T>()
    {
        ObjectDisposedException.ThrowIf(_closed, this);
        if (!_contracts.TryGetValue(typeof(T), out object? contracts))
        {
            contracts = Array.AsReadOnly(_features.OfType<T>().ToArray());
            _contracts.Add(typeof(T), contracts);
        }
        return (IReadOnlyList<T>)contracts;
    }

    public string Describe() => string.Join(", ", _features.Select(feature => feature.GetType().Name)) +
        "; state: " + string.Join("; ", _stateDescriptions.Select(describe => describe()));

    public void Dispose()
    {
        if (_closed)
            return;
        _closed = true;
        List<Exception> errors = [];
        void Release(Action action)
        {
            try { action(); }
            catch (Exception exception) { errors.Add(exception); }
        }
        foreach (object feature in _attached)
        {
            Release(() => _detach?.Invoke(feature));
        }
        _attached.Clear();
        Release(() => _lifetime.Cancel());
        for (int i = _cleanup.Count - 1; i >= 0; i--)
        {
            Release(_cleanup[i]);
        }
        for (int i = _features.Count - 1; i >= 0; i--)
        {
            if (_features[i] is IDisposable disposable)
                Release(disposable.Dispose);
        }
        _lifetime.Dispose();
        if (errors.Count > 0)
            throw new AggregateException("Scope cleanup failed.", errors);
    }
}
