using System.Runtime.InteropServices;

namespace ScreenDemoRecorder;

internal static class DiagnosticChecks
{
    internal static async Task RunAsync()
    {
        CheckRetention();
        await Task.Delay(2500);
        // This stall exists only in an explicitly selected diagnostic test command.
        using (DiagnosticTrace.Step("SELF_TEST simulated UI stall")) Thread.Sleep(7000);
        try { throw new COMException("SELF_TEST wrong-thread exception", unchecked((int)0x8001010E)); }
        catch (COMException error) { DiagnosticTrace.Error("SELF_TEST caught exception", error); }
        await Task.Delay(2500);
        using var reader = new StreamReader(new FileStream(DiagnosticTrace.LogPath, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite));
        var content = reader.ReadToEnd();
        Require(content.Contains("UI_UNRESPONSIVE") && content.Contains("0x8001010E") &&
            content.Contains("ACTIVE #") && content.Contains("DiagnosticChecks.RunAsync"),
            "The watchdog did not persist the UI stall, operation, HRESULT and exception stack.");
        DiagnosticTrace.Write("SELF_TEST PASS: rotation, retention, UI hang, active operation, HRESULT and stack persisted.");
    }

    internal static async Task WaitForForcedStopAsync()
    {
        await Task.Delay(2500);
        DiagnosticTrace.Write("FORCE_STOP_TEST_READY");
        using var step = DiagnosticTrace.Step("SELF_TEST forced-stop UI stall");
        Thread.Sleep(30000);
        throw new TimeoutException("The parent test did not terminate the process during the simulated stall.");
    }

    private static void CheckRetention()
    {
        var directory = Path.Combine(Path.GetDirectoryName(DiagnosticTrace.LogPath)!, "retention-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var unrelated = Path.Combine(directory, "diagnostic-user-notes.log");
        File.WriteAllText(unrelated, "keep this file");
        using (var file = new DiagnosticLogFile(directory, maximumBytes: 512, runFiles: 3))
        {
            for (var index = 0; index < 100; index++) file.Write($"entry {index}: " + new string('x', 80));
            var logs = Directory.GetFiles(directory, "*.log").Where(path => path != unrelated).ToArray();
            Require(logs.Length == 3 && logs.All(path => new FileInfo(path).Length <= 512), "Rotation exceeded its file/byte limit.");
            using var reader = new StreamReader(new FileStream(file.CurrentPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
            Require(reader.ReadToEnd().Contains("entry 99"), "Rotation lost the latest entry.");
            // Retention must not unlink an active writer.
            DiagnosticLogFile.Prune(directory, DateTime.UtcNow, 0);
            Require(File.Exists(file.CurrentPath), "Retention deleted the active log.");
        }
        foreach (var path in Directory.GetFiles(directory, "*.log").Where(path => path != unrelated))
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-8));
        DiagnosticLogFile.Prune(directory, DateTime.UtcNow, 20);
        Require(Directory.GetFiles(directory).SequenceEqual(new[] { unrelated }), "Retention did not remove expired logs or touched an unrelated file.");
        for (var index = 0; index < 24; index++)
        {
            using var file = new DiagnosticLogFile(directory);
            file.Write("retention count test");
        }
        Require(Directory.GetFiles(directory).Length == 21, "Global retention did not keep exactly 20 owned logs and the unrelated file.");
        DiagnosticTrace.Write("RETENTION_TEST PASS: byte/run/global/age limits and active/unrelated-file protection.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
