using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace ScreenDemoRecorder;

internal static class DiagnosticTrace
{
    private static readonly object WriteLock = new();
    private static readonly ConcurrentDictionary<long, Activity> Active = new();
    private static readonly ConcurrentDictionary<string, long> Counts = new();
    private static readonly ManualResetEventSlim StopSignal = new(false);
    private static DiagnosticLogFile? writer;
    internal static string LogPath => writer?.CurrentPath ?? "";
    private static long nextId;
    private static long lastUiTick;
    private static int uiPending;
    private static Dispatcher? ui;
    private static int firstChanceCount;
    [ThreadStatic] private static bool loggingError;
    [ThreadStatic] internal static Action<string>? BeforeNativeCallForChecks;

    [ModuleInitializer]
    internal static void Initialize()
    {
        var arguments = Environment.GetCommandLineArgs();
        var testDirectory = arguments.Length == 3 && arguments[1] is
            "--diagnostic-log-self-test" or "--diagnostic-force-stop-test" or "--smoke-test" or
            "--cpu-pipeline-smoke-test" or "--startup-probe" or "--build-flavor-check" ? Path.GetFullPath(arguments[2]) : null;
        foreach (var root in new[] { Path.Combine(testDirectory ?? AppContext.BaseDirectory, "diagnostics"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Screen Demo Recorder", "Diagnostics") })
        {
            try
            {
                writer = new DiagnosticLogFile(root);
                break;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
        if (writer is null) throw new IOException("The diagnostic build cannot create its log file.");
        var version = typeof(DiagnosticTrace).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .Cast<System.Reflection.AssemblyInformationalVersionAttribute>().Single().InformationalVersion;
        Write($"START diagnostic version={version}; pid={Environment.ProcessId}");
        Write("PROBE readback-v2: native readback calls and capture-state lock waits are instrumented; capture behavior is unchanged.");
        Write($"ENV OS={RuntimeInformation.OSDescription}; OSVersion={Environment.OSVersion}; " +
            $"runtime={RuntimeInformation.FrameworkDescription}; arch={RuntimeInformation.ProcessArchitecture}; " +
            $"processors={Environment.ProcessorCount}; 64bit={Environment.Is64BitProcess}");
        Write("PRIVACY No screen pixels, key text, click positions, or label text are logged. Exceptions may contain local paths.");
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Error("UNHANDLED terminating=" + e.IsTerminating, e.ExceptionObject as Exception);
        AppDomain.CurrentDomain.FirstChanceException += (_, e) =>
        {
            if (!loggingError && (e.Exception.HResult == unchecked((int)0x8001010E) ||
                e.Exception is COMException) && Interlocked.Increment(ref firstChanceCount) <= 40)
                Error("FIRST_CHANCE_COM", e.Exception);
        };
        TaskScheduler.UnobservedTaskException += (_, e) => Error("UNOBSERVED_TASK", e.Exception);
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            StopSignal.Set();
            Write("PROCESS_EXIT");
            lock (WriteLock) writer?.Dispose();
        };
        new Thread(Watchdog) { IsBackground = true, Name = "Diagnostic watchdog" }.Start();
    }

    internal static void Attach(Dispatcher dispatcher)
    {
        ui = dispatcher;
        Interlocked.Exchange(ref lastUiTick, Environment.TickCount64);
        dispatcher.UnhandledException += (_, e) => Error("UI_UNHANDLED", e.Exception);
        Write("UI dispatcher attached");
    }

    internal static IDisposable Step(string name, bool quiet = false) => new Activity(name, quiet);
    // Persist the first entry/exit, then use watchdog snapshots for stalled repeated calls.
    internal static IDisposable NativeStep(string name) => Step(name, quiet: Counts.ContainsKey(name + ".completed"));
    internal static T NativeCall<T>(string name, Func<T> action)
    {
        using var step = NativeStep(name);
        BeforeNativeCallForChecks?.Invoke(name);
        return action();
    }
    internal static void NativeCall(string name, Action action)
    {
        using var step = NativeStep(name);
        BeforeNativeCallForChecks?.Invoke(name);
        action();
    }
    internal static T Call<T>(string name, Func<T> action, bool quiet = false)
    {
        using var step = Step(name, quiet);
        return action();
    }
    internal static void Count(string name) => Counts.AddOrUpdate(name, 1, (_, n) => n + 1);
    internal static void Write(string message)
    {
        lock (WriteLock)
        {
            try
            {
                writer?.Write($"{DateTime.UtcNow:O} T{Environment.CurrentManagedThreadId} " +
                    $"{Thread.CurrentThread.GetApartmentState()} {message}");
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ObjectDisposedException) { }
        }
    }

    internal static void Error(string context, Exception? error)
    {
        if (loggingError) return;
        loggingError = true;
        try { Write($"ERROR {context} HRESULT=0x{error?.HResult:X8}\n{error}"); }
        finally { loggingError = false; }
    }

    private static void Watchdog()
    {
        using var process = Process.GetCurrentProcess();
        var previous = TimeSpan.Zero;
        try
        {
            using var processor = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            Write("CPU " + processor?.GetValue("ProcessorNameString"));
            using var display = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            foreach (var subkey in display?.GetSubKeyNames() ?? [])
            {
                if (!int.TryParse(subkey, out _)) continue;
                using var adapter = display!.OpenSubKey(subkey);
                if (adapter?.GetValue("DriverDesc") is string description)
                    Write($"DISPLAY {description}; driver={adapter.GetValue("DriverVersion")}");
            }
        }
        catch (Exception error) { Error("hardware metadata", error); }
        while (!StopSignal.Wait(2000))
        {
            try
            {
                var now = Environment.TickCount64;
                var dispatcher = ui;
                var lag = dispatcher is null ? -1 : now - Interlocked.Read(ref lastUiTick);
                if (dispatcher is not null && !dispatcher.HasShutdownStarted && Interlocked.CompareExchange(ref uiPending, 1, 0) == 0)
                    dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                    {
                        Interlocked.Exchange(ref lastUiTick, Environment.TickCount64);
                        Interlocked.Exchange(ref uiPending, 0);
                    }));
                process.Refresh();
                var cpu = process.TotalProcessorTime;
                Write($"HEARTBEAT uiLagMs={lag} {(lag >= 5000 ? "UI_UNRESPONSIVE" : "")} " +
                    $"cpuDeltaMs={(cpu - previous).TotalMilliseconds:F0}; memoryMB={process.PrivateMemorySize64 / 1048576}; threads={process.Threads.Count}");
                previous = cpu;
                foreach (var activity in Active.Values.OrderBy(a => a.Id))
                    Write($"ACTIVE #{activity.Id} {activity.Name}; owner=T{activity.ThreadId}/{activity.Apartment}; elapsedMs={now - activity.Start}");
                Write("COUNTERS " + string.Join("; ", Counts.OrderBy(p => p.Key).Select(p => $"{p.Key}={p.Value}")));
            }
            catch (Exception error) { Error("watchdog", error); }
        }
    }

    private sealed class Activity : IDisposable
    {
        internal readonly long Id = Interlocked.Increment(ref nextId);
        internal readonly string Name;
        internal readonly long Start = Environment.TickCount64;
        internal readonly int ThreadId = Environment.CurrentManagedThreadId;
        internal readonly ApartmentState Apartment = Thread.CurrentThread.GetApartmentState();
        private readonly bool quiet;
        internal Activity(string name, bool quiet)
        {
            Name = name;
            this.quiet = quiet;
            Active[Id] = this;
            if (!quiet) Write($"BEGIN #{Id} {name}");
        }
        public void Dispose()
        {
            if (!Active.TryRemove(Id, out _)) return;
            var elapsed = Environment.TickCount64 - Start;
            Count(Name + ".completed");
            if (!quiet || elapsed >= 5000) Write($"END #{Id} {Name}; elapsedMs={elapsed}");
        }
    }

}
