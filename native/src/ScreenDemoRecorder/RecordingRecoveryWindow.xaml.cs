using System.ComponentModel;
using System.IO;
using System.Windows;
using ScreenDemoRecorder.Capture;

namespace ScreenDemoRecorder;

public partial class RecordingRecoveryWindow : Window
{
    private readonly string[] sessions;
    private readonly CancellationTokenSource cancellation = new();
    private bool running = true;
    private bool allowClose;

    public List<string> RecoveredPaths { get; } = [];

    internal RecordingRecoveryWindow(string[] sessionDirectories)
    {
        sessions = sessionDirectories;
        InitializeComponent();
        SourceInitialized += (_, _) => NativeDesktop.Exclude(this);
        ContentRendered += StartRecovery;
    }

    private async void StartRecovery(object? sender, EventArgs e)
    {
        ContentRendered -= StartRecovery;
        try
        {
            for (var index = 0; index < sessions.Length; index++)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                SessionText.Text = $"Recording {index + 1} of {sessions.Length}";
                Progress.Value = 0;
                ProgressText.Text = Path.GetFileName(sessions[index]);
                IProgress<CpuRenderProgress> progress = new Progress<CpuRenderProgress>(update =>
                {
                    Progress.Value = update.Percent;
                    ProgressText.Text = $"Rendering overlays · {update.Percent:F0}% · {update.Frames} / {update.TotalFrames} frames";
                });
                RecoveredPaths.Add(await RecordingRecovery.RenderAsync(sessions[index], progress.Report,
                    cancellation.Token));
            }
            SessionText.Text = $"Recovered {RecoveredPaths.Count} recording(s).";
            Progress.Value = 100;
            ProgressText.Text = RecoveredPaths.Count == 0 ? "No recordings were recovered." :
                $"Saved: {Path.GetFileName(RecoveredPaths[^1])}";
        }
        catch (OperationCanceledException)
        {
            SessionText.Text = "Recovery cancelled";
            ProgressText.Text = "Unfinished sessions were retained and can be recovered on the next launch.";
        }
        catch (Exception error)
        {
            SessionText.Text = "Recovery stopped";
            ProgressText.Text = $"{error.Message}\n\nThe unfinished session was retained.";
        }
        finally
        {
            running = false;
            ActionButton.Content = "Close";
        }
    }

    private void ActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (running)
        {
            cancellation.Cancel();
            ActionButton.IsEnabled = false;
            ProgressText.Text = "Cancelling…";
            return;
        }
        allowClose = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!allowClose && running)
        {
            e.Cancel = true;
            cancellation.Cancel();
            ActionButton.IsEnabled = false;
            ProgressText.Text = "Cancelling…";
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        cancellation.Dispose();
        base.OnClosed(e);
    }
}
