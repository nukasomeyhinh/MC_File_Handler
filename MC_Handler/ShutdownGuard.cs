using System.Runtime.InteropServices;

public static class ShutdownGuard
{
    private delegate bool ConsoleEventDelegate(int eventType);
    private static ConsoleEventDelegate? _handler;

    public static Func<Task>? OnShutdown;

    public static int ShutdownTimeoutMs { get; set; } = 30000;

    private static readonly ManualResetEventSlim _shutdownStarted = new(false);
    private static readonly ManualResetEventSlim _shutdownCompleted = new(false);

    private const int CTRL_C_EVENT = 0;
    private const int CTRL_CLOSE_EVENT = 2;
    private const int CTRL_LOGOFF_EVENT = 5;
    private const int CTRL_SHUTDOWN_EVENT = 6;

    [DllImport("Kernel32")]
    private static extern bool SetConsoleCtrlHandler(ConsoleEventDelegate handler, bool add);

    private static bool Handler(int eventType)
    {
        if (eventType == CTRL_CLOSE_EVENT ||
            eventType == CTRL_LOGOFF_EVENT ||
            eventType == CTRL_SHUTDOWN_EVENT ||
            eventType == CTRL_C_EVENT)
        {
            if (!_shutdownStarted.IsSet)
            {
                _shutdownStarted.Set();
                Console.WriteLine("\n[ShutdownGuard] Shutdown signal detected — running cleanup...");

                Task.Run(async () =>
                {
                    try
                    {
                        if (OnShutdown != null)
                        {
                            await OnShutdown().ConfigureAwait(false);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[ShutdownGuard] Error in OnShutdown: {ex.Message}");
                    }
                    finally
                    {
                        _shutdownCompleted.Set();
                    }
                });
            }

            bool finished = _shutdownCompleted.Wait(ShutdownTimeoutMs);
            if (!finished)
                Console.WriteLine($"[ShutdownGuard] Timed out after {ShutdownTimeoutMs / 1000}s — exiting anyway.");

            return true;
        }

        return false;
    }

    public static void Enable(int timeoutMs = 30000)
    {
        ShutdownTimeoutMs = timeoutMs;
        _handler = new ConsoleEventDelegate(Handler);
        SetConsoleCtrlHandler(_handler, true);

        AppDomain.CurrentDomain.ProcessExit += (s, e) =>
        {
            if (OnShutdown != null && !_shutdownStarted.IsSet)
            {
                _shutdownStarted.Set();
                Task.Run(async () =>
                {
                    try
                    {
                        await OnShutdown();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[ShutdownGuard] Error in ProcessExit OnShutdown: {ex.Message}");
                    }
                    finally
                    {
                        _shutdownCompleted.Set();
                    }
                }).Wait(ShutdownTimeoutMs);
            }
        };

        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            Handler(CTRL_C_EVENT);
        };
    }
}
