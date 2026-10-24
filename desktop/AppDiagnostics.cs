using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Threading;

namespace FuniturenEditor.Desktop;

internal static class AppDiagnostics
{
#if INTERNAL_DIAGNOSTICS
    private static readonly object FileLock = new();
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ShadierPond", "FuniturenEditor", "Diagnostics",
        $"freeze-{DateTime.Now:yyyyMMdd-HHmmss}-{Environment.ProcessId}.log");
    private static DispatcherTimer? uiHeartbeat;
    private static System.Threading.Timer? watchdog;
    private static long lastUiHeartbeat;
    private static long lastStallLog;
    private static int isCheckingHeartbeat;
    private static int initialized;
#endif

    public static IDisposable Measure(string operation, Func<string>? details = null)
    {
#if INTERNAL_DIAGNOSTICS
        EnsureInitialized();
        Write("START", operation, details?.Invoke() ?? "");
        return new OperationScope(operation);
#else
        return EmptyScope.Instance;
#endif
    }

    public static void Event(string name, string details = "")
    {
#if INTERNAL_DIAGNOSTICS
        EnsureInitialized();
        Write("EVENT", name, details);
#endif
    }

    public static void Exception(string name, Exception exception)
    {
#if INTERNAL_DIAGNOSTICS
        EnsureInitialized();
        Write("ERROR", name, exception.ToString());
#else
        Trace.WriteLine($"{name}: {exception}");
#endif
    }

    public static void StartUiThreadMonitor(Dispatcher dispatcher)
    {
#if INTERNAL_DIAGNOSTICS
        EnsureInitialized();
        Interlocked.Exchange(ref lastUiHeartbeat, Stopwatch.GetTimestamp());
        uiHeartbeat = new DispatcherTimer(DispatcherPriority.ApplicationIdle, dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        uiHeartbeat.Tick += (_, _) => Interlocked.Exchange(ref lastUiHeartbeat, Stopwatch.GetTimestamp());
        uiHeartbeat.Start();
        watchdog = new System.Threading.Timer(CheckUiHeartbeat, null, 1000, 1000);
#endif
    }

    public static void StopUiThreadMonitor()
    {
#if INTERNAL_DIAGNOSTICS
        uiHeartbeat?.Stop();
        uiHeartbeat = null;
        watchdog?.Dispose();
        watchdog = null;
#endif
    }

#if INTERNAL_DIAGNOSTICS
    private static void EnsureInitialized()
    {
        if (Interlocked.Exchange(ref initialized, 1) != 0)
            return;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
        }
        catch (IOException ex)
        {
            Trace.WriteLine($"Could not create diagnostics directory: {ex.Message}");
            return;
        }
        catch (UnauthorizedAccessException ex)
        {
            Trace.WriteLine($"Could not create diagnostics directory: {ex.Message}");
            return;
        }

        Write("INFO", "app.start", $"processId={Environment.ProcessId};runtime={Environment.Version}");
        Trace.WriteLine($"Internal diagnostics log: {LogPath}");
    }

    private static void CheckUiHeartbeat(object? state)
    {
        if (Interlocked.Exchange(ref isCheckingHeartbeat, 1) != 0)
            return;

        try
        {
            var now = Stopwatch.GetTimestamp();
            var heartbeat = Interlocked.Read(ref lastUiHeartbeat);
            var delay = Stopwatch.GetElapsedTime(heartbeat, now);
            if (delay < TimeSpan.FromSeconds(2))
                return;

            var lastLog = Interlocked.Read(ref lastStallLog);
            if ((lastLog != 0 && Stopwatch.GetElapsedTime(lastLog, now) < TimeSpan.FromSeconds(5)) ||
                Interlocked.CompareExchange(ref lastStallLog, now, lastLog) != lastLog)
                return;

            Write("STALL", "ui.thread", $"heartbeatDelayMs={delay.TotalMilliseconds:F0}");
        }
        finally
        {
            Volatile.Write(ref isCheckingHeartbeat, 0);
        }
    }

    private static void Write(string level, string operation, string details)
    {
        var line = $"{DateTimeOffset.Now:O}\t{level}\t{operation}\t{details.Replace('\r', ' ').Replace('\n', ' ')}{Environment.NewLine}";
        try
        {
            lock (FileLock)
                File.AppendAllText(LogPath, line, Encoding.UTF8);
        }
        catch (IOException ex)
        {
            Trace.WriteLine($"Could not write diagnostics log: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            Trace.WriteLine($"Could not write diagnostics log: {ex.Message}");
        }
    }

    private sealed class OperationScope(string operation) : IDisposable
    {
        private readonly Stopwatch stopwatch = Stopwatch.StartNew();
        private int disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
                return;

            Write("END", operation, $"elapsedMs={stopwatch.Elapsed.TotalMilliseconds:F1}");
        }
    }
#endif

    private sealed class EmptyScope : IDisposable
    {
        public static EmptyScope Instance { get; } = new();
        public void Dispose() { }
    }
}
