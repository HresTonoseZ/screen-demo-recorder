using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using ScreenDemoRecorder.Capture;
using ScreenDemoRecorder.Core.Services;
using ScreenDemoRecorder.Core.Models;

namespace ScreenDemoRecorder;

public partial class MainWindow
{
    private CpuRecordingSession? recording;
    private CancellationTokenSource? countdown;
    private CancellationTokenSource? exportCancellation;
    private bool closingAfterRecording;
    private Task? recordingTask;
    private bool recordingBusy;
    private bool dispatchingRecordingCommand;
    internal Task? ActiveRecordingTask => recordingTask;

    private void RecordButton_Click(object sender, RoutedEventArgs e) => ExecuteRecordingCommand(RecorderCommand.ToggleRecording);

    internal void ExecuteRecordingCommand(RecorderCommand command)
    {
        if (dispatchingRecordingCommand || profileOperation || closeAllowed || editingHotkeys || !IsEnabled) return;
        dispatchingRecordingCommand = true;
        try
        {
            switch (command)
            {
                case RecorderCommand.ToggleRecording:
                    if (recordingBusy)
                    {
                        if (exportCancellation is not null) break;
                        if (recording is null) countdown?.Cancel();
                        else if (!recording.IsStopped) { recording.Stop(); UpdateSessionStatus(); }
                    }
                    else if (RecordButton.IsEnabled) recordingTask = RecordAsync();
                    else StatusText.Text = RecordButton.ToolTip?.ToString() ?? "Select a valid recording source first.";
                    break;
                case RecorderCommand.TogglePause:
                    if (recording is { IsStopped: false }) { recording.TogglePause(); UpdateSessionStatus(); }
                    break;
                case RecorderCommand.CancelRecording:
                    CancelCurrentRecording();
                    break;
            }
        }
        finally { dispatchingRecordingCommand = false; }
    }

    private async Task RecordAsync()
    {
        recordingBusy = true;
        countdown = new CancellationTokenSource();
        ProfilePanel.IsEnabled = SettingsPanel.IsEnabled = false;
        ShortcutsButton.IsEnabled = false;
        ApplicationSettingsButton.IsEnabled = false;
        CancelButton.Visibility = Visibility.Visible;
        CancelButton.IsEnabled = true;
        CancelButton.ToolTip = "Discard this recording";
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += RecordingTick;
        try
        {
            await SaveNowAsync();
            var snapshot = store.GetActiveProfile();
            var target = CaptureTargetFactory.Create(snapshot.Capture, displays, selectedWindow);
            for (var seconds = snapshot.Capture.CountdownSeconds; seconds > 0; seconds--)
            {
                RecordButton.Content = $"Starting in {seconds}…";
                StatusText.Text = "Click Cancel to stop the countdown";
                await Task.Delay(1000, countdown.Token);
            }
            countdown.Token.ThrowIfCancellationRequested();
            if (PreviewMode) throw new InvalidOperationException("Preview windows cannot capture the screen.");
            target = CaptureTargetFactory.Create(snapshot.Capture, displays, selectedWindow);
            var area = target.Area;
            if (snapshot.Output.Format == OutputFormat.Gif)
                _ = new GifExportPlan(area.Width, area.Height, TimeSpan.FromSeconds(1), snapshot.Capture, snapshot.Output);
            RecordButton.Content = "Starting…";
            StatusText.Text = "Preparing clean CPU capture…";
            var overlays = RecordingOverlayPipeline.Create(snapshot, area.Width, area.Height);
            boundary?.Dispose(); boundary = null;
            desktopOverlay?.Dispose(); desktopOverlay = null;
            NativeDesktop.FlushComposition();
            recording = new CpuRecordingSession(target.Item, area, snapshot,
                snapshot.Capture.AutomaticFps ? 30 : snapshot.Capture.RecordingFps,
                overlays,
                screenPointMapper: target.MapScreenPoint,
                sourceValidation: target.Validate,
                liveKeystroke: (chord, time) => desktopOverlay?.AddKeystroke(chord, time),
                liveClick: (position, button, time) => desktopOverlay?.AddMouseClick(position, button, time));
            var startupTimeout = Task.Delay(TimeSpan.FromSeconds(45));
            var startup = await Task.WhenAny(recording.Ready, recording.Completion, startupTimeout);
            if (startup == startupTimeout)
            {
                recording.Stop(discard: true);
                throw new TimeoutException("Screen capture or the H.264 encoder did not start within 45 seconds.");
            }
            if (startup == recording.Completion)
                await recording.Completion;
            if (await recording.Ready)
            {
                PauseButton.Visibility = Visibility.Visible;
                RefreshBoundary();
                UpdateSessionStatus();
                timer.Start();
            }
            var path = await recording.Completion;
            timer.Stop();
            recording = null;
            PauseButton.Visibility = Visibility.Collapsed;
            RefreshBoundary();
            if (path is not null && snapshot.Output.Format == OutputFormat.Gif && !closingAfterRecording)
            {
                path = await ExportGifAsync(path, area, snapshot);
                if (path is null) return;
            }
            var recentListUpdated = true;
            if (path is not null)
            {
                try { await store.AddRecentFileAsync(path); }
                catch { recentListUpdated = false; }
            }
            StatusText.Text = path is null ? "Recording cancelled" : $"Saved: {Path.GetFileName(path)}";
            if (!recentListUpdated) StatusText.Text += " · recent list was not updated";
            StatusText.ToolTip = path;
            if (path is not null) NotifyRecordingSaved(path);
            if (path is not null && snapshot.Output.OpenFolderAfterSave)
                Process.Start(new ProcessStartInfo(Path.GetDirectoryName(path)!) { UseShellExecute = true });
        }
        catch (OperationCanceledException) { StatusText.Text = "Countdown cancelled"; }
        catch (RecordingFailureException error)
        {
            StatusText.Text = "Recording stopped";
            if (closingAfterRecording) return;
            var recovery = new RecordingFailureWindow(error) { Owner = this };
            if (recovery.ShowDialog() == true && recovery.UseSafeSettings)
            {
                var safer = store.GetActiveProfile();
                safer.Output.Quality = QualityPreset.Efficient;
                safer.Output.Mp4Width = safer.Output.Mp4Width == 0 ? 1280 : Math.Min(safer.Output.Mp4Width, 1280);
                await store.UpdateActiveAsync(safer);
                LoadActiveProfile();
                StatusText.Text = "Safer MP4 settings applied · press Record to try again";
            }
        }
        catch (Exception error) { StatusText.Text = "Recording failed"; ShowError(error, "Cannot Record"); }
        finally
        {
            timer.Stop(); timer.Tick -= RecordingTick;
            recording = null;
            countdown.Dispose(); countdown = null;
            exportCancellation?.Dispose(); exportCancellation = null;
            ExportProgress.Visibility = Visibility.Collapsed;
            recordingBusy = false;
            ProfilePanel.IsEnabled = SettingsPanel.IsEnabled = true;
            ShortcutsButton.IsEnabled = true;
            ApplicationSettingsButton.IsEnabled = true;
            PauseButton.Visibility = CancelButton.Visibility = Visibility.Collapsed;
            UpdateRecordLabel(); RefreshBoundary();
        }
    }

    private void RecordingTick(object? sender, EventArgs e) => UpdateSessionStatus();

    private void UpdateSessionStatus()
    {
        if (recording is null) return;
        var finishing = recording.IsStopped;
        RecordButton.Content = recording.Stage == CpuRecordingStage.Rendering ? "Rendering overlays…" :
            finishing ? "Finalizing recording…" : "Stop & save";
        RecordButton.IsEnabled = !finishing;
        PauseButton.Content = recording.IsPaused ? "Resume" : "Pause";
        PauseButton.IsEnabled = !finishing;
        CancelButton.IsEnabled = !finishing;
        var limit = profile.Capture.MaximumDurationSeconds > 0 ? $" / {TimeSpan.FromSeconds(profile.Capture.MaximumDurationSeconds):hh\\:mm\\:ss}" : "";
        var encoder = recording.UsesSoftwareEncoder ? " · software encoder" : "";
        var liveWarning = liveOverlayWarning is null ? "" : " · live overlay disabled";
        var stage = recording.Stage switch
        {
            CpuRecordingStage.Rendering => "Rendering overlays",
            CpuRecordingStage.Finalizing => "Finalizing recording",
            _ when recording.IsPaused => "Paused",
            _ => "Recording",
        };
        StatusText.Text = $"{stage} · {recording.Elapsed:hh\\:mm\\:ss}{limit}{encoder}{liveWarning}";
        StatusText.ToolTip = liveOverlayWarning;
    }

    private void PauseButton_Click(object sender, RoutedEventArgs e) => ExecuteRecordingCommand(RecorderCommand.TogglePause);

    private void CancelButton_Click(object sender, RoutedEventArgs e) => ExecuteRecordingCommand(RecorderCommand.CancelRecording);

    private void CancelCurrentRecording()
    {
        if (exportCancellation is not null)
        {
            exportCancellation.Cancel();
            CancelButton.IsEnabled = false;
            StatusText.Text = "Cancelling GIF export · the MP4 will be kept";
            return;
        }
        if (recording is null) { countdown?.Cancel(); return; }
        var active = recording;
        if (active.IsStopped) return;
        if (MessageBox.Show(this, "Discard the current recording? No video will be saved.", "Discard Recording",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        if (active.IsStopped) return;
        active.Stop(discard: true);
        UpdateSessionStatus();
    }

    private async Task<string?> ExportGifAsync(string sourcePath, PixelRect area, RecorderProfile snapshot)
    {
        exportCancellation = new CancellationTokenSource();
        RecordButton.Content = "Exporting GIF…";
        RecordButton.IsEnabled = false;
        CancelButton.IsEnabled = true;
        CancelButton.ToolTip = "Cancel GIF export and keep the MP4";
        ExportProgress.Value = 0;
        ExportProgress.Visibility = Visibility.Visible;
        StatusText.Text = "Preparing GIF export…";
        var operation = exportCancellation;
        var progress = new Progress<GifProgress>(update =>
        {
            if (exportCancellation != operation || operation.IsCancellationRequested) return;
            ExportProgress.Value = update.Percent;
            StatusText.Text = $"Exporting GIF · {update.Percent:F0}% · {update.Frames} / {update.TotalFrames} frames";
        });
        try
        {
            var path = await GifExport.RunAsync(sourcePath, new PixelRect(0, 0, area.Width, area.Height), snapshot, progress, operation.Token);
            if (!snapshot.Output.KeepSourceVideo)
            {
                try { File.Delete(sourcePath); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    ShowError(new IOException($"The GIF was saved, but its source MP4 could not be removed.\n{sourcePath}\n\n{error.Message}", error), "MP4 Kept");
                }
            }
            return path;
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = $"GIF cancelled · MP4 kept: {Path.GetFileName(sourcePath)}";
            StatusText.ToolTip = sourcePath;
            return null;
        }
        catch (Exception error)
        {
            StatusText.Text = $"GIF failed · MP4 kept: {Path.GetFileName(sourcePath)}";
            StatusText.ToolTip = sourcePath;
            ShowError(new IOException($"{error.Message}\n\nYour MP4 recording is safe:\n{sourcePath}", error), "Cannot Export GIF");
            return null;
        }
        finally
        {
            exportCancellation = null;
            operation.Dispose();
        }
    }
}
