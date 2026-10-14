using System.Runtime.InteropServices;

namespace LocaleGameHub.Services;

/// <summary>
/// Keeps a single VNAR library window per Windows session. A second launch signals
/// the running instance to come to the front and then exits, so two windows never
/// hold different in-memory copies of the library.
/// Game shortcut launches ("--launch") do not use this; they run alongside.
/// </summary>
public static class SingleInstanceService
{
    private const string InstanceMutexName = @"Local\JAVCIF.VNAR.Launcher.Instance";
    private const string ActivateEventName = @"Local\JAVCIF.VNAR.Launcher.Activate";
    private const int AsfwAny = -1;

    // Held for the whole lifetime of the primary instance.
    private static Mutex? _instanceMutex;
    private static EventWaitHandle? _activateEvent;

    /// <summary>
    /// Returns true if this process is the primary library instance. Otherwise asks
    /// the running instance to show itself and returns false.
    /// </summary>
    public static bool TryClaimPrimary()
    {
        bool createdNew;
        try
        {
            _instanceMutex = new Mutex(initiallyOwned: true, InstanceMutexName, out createdNew);
        }
        catch (UnauthorizedAccessException)
        {
            // The mutex exists but belongs to an instance with different rights
            // (e.g. elevated). Treat it as already running.
            createdNew = false;
        }

        if (createdNew)
        {
            _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
            return true;
        }

        _instanceMutex?.Dispose();
        _instanceMutex = null;
        SignalPrimary();
        return false;
    }

    /// <summary>
    /// Starts listening for activation requests from later launches.
    /// <paramref name="onActivate"/> runs on a background thread; marshal to the UI.
    /// </summary>
    public static void ListenForActivation(Action onActivate)
    {
        var handle = _activateEvent ?? throw new InvalidOperationException("Call TryClaimPrimary first.");
        var thread = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    handle.WaitOne();
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                onActivate();
            }
        })
        {
            IsBackground = true,
            Name = "VNAR single-instance listener"
        };
        thread.Start();
    }

    private static void SignalPrimary()
    {
        try
        {
            // Let the running instance take the foreground; Windows would otherwise
            // only flash its taskbar button.
            AllowSetForegroundWindow(AsfwAny);
            using var handle = EventWaitHandle.OpenExisting(ActivateEventName);
            handle.Set();
        }
        catch (Exception ex) when (ex is WaitHandleCannotBeOpenedException or UnauthorizedAccessException)
        {
            // The other instance is starting up or shutting down; nothing else to do.
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(int processId);
}
