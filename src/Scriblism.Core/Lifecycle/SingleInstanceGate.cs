namespace Scriblism.Core.Lifecycle;

/// <summary>Session-wide identity independent of executable location or version.</summary>
public sealed class SingleInstanceGate : IDisposable
{
    private readonly Mutex _presence;
    private readonly EventWaitHandle _activation;
    private readonly RegisteredWaitHandle? _registration;
    private bool _disposed;

    public SingleInstanceGate(string name, Action onActivation, Action? beforeActivationSignal = null)
    {
        // Presence is handle-owned, not thread-owned; async callers can dispose safely.
        // The event exists before presence is published, so early clicks are retained.
        _activation = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\" + name + ".Activate");
        _presence = new Mutex(false, @"Local\" + name + ".Presence", out var first);
        IsPrimary = first;
        if (first)
            _registration = ThreadPool.RegisterWaitForSingleObject(_activation, (_, _) => onActivation(), null, Timeout.Infinite, false);
        else
        {
            beforeActivationSignal?.Invoke();
            _activation.Set();
        }
    }

    public bool IsPrimary { get; }

    public static void Signal(string name)
    {
        if (!OperatingSystem.IsWindows()) return;
        if (EventWaitHandle.TryOpenExisting(@"Local\" + name + ".Activate", out var activation))
            using (activation) activation.Set();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _registration?.Unregister(null);
        _presence.Dispose();
        _activation.Dispose();
    }
}
