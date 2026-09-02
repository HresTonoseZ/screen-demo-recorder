using System.Windows;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ScreenDemoRecorder.Core.Models;
using ScreenDemoRecorder.Core.Services;
using ScreenDemoRecorder.Capture;
using ScreenDemoRecorder.Overlays;

namespace ScreenDemoRecorder;

public partial class App : Application
{
    private Mutex? instanceMutex;
    private bool ownsMutex;

    protected override void OnExit(ExitEventArgs e)
    {
        ApplicationThemeManager.Shutdown();
        if (ownsMutex) instanceMutex?.ReleaseMutex();
        instanceMutex?.Dispose();
        base.OnExit(e);
    }

    private void Window_Loaded_ApplyTheme(object sender, RoutedEventArgs e)
    {
        if (sender is Window window) ApplicationThemeManager.ApplyToWindow(window);
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        var startupTimer = Stopwatch.StartNew();
        base.OnStartup(e);
        ApplicationThemeManager.Initialize();
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        string? smokeDirectory = e.Args is ["--smoke-test", var output] ? Path.GetFullPath(output) : null;
        string? recordingCheckDirectory = e.Args is ["--recording-smoke-test", var recordingOutput] ? Path.GetFullPath(recordingOutput) : null;
        string? cpuPipelineCheckDirectory = e.Args is ["--cpu-pipeline-smoke-test", var cpuPipelineOutput] ? Path.GetFullPath(cpuPipelineOutput) : null;
        string? startupProbeDirectory = e.Args is ["--startup-probe", var startupOutput] ? Path.GetFullPath(startupOutput) : null;
        try
        {
            if (cpuPipelineCheckDirectory is not null)
            {
                await CpuPipelineSmokeCheck.RunAsync(cpuPipelineCheckDirectory);
                Shutdown(0);
                return;
            }
            if (recordingCheckDirectory is not null)
            {
                await RecordingSmokeCheck.RunAsync(recordingCheckDirectory);
                Shutdown(0);
                return;
            }
            if (smokeDirectory is not null)
            {
                ApplicationThemeManager.Apply(ApplicationTheme.Dark);
                if (!NativeDesktop.IsPerMonitorV2())
                    throw new InvalidOperationException("The application did not start with Per-Monitor V2 DPI awareness.");
                var dpiReport = CheckConnectedDisplayDpi();
                Directory.CreateDirectory(smokeDirectory);
                LabelRenderChecks.Run(smokeDirectory);
                KeystrokeRenderChecks.Run(smokeDirectory);
                ClickRenderChecks.Run(smokeDirectory);
                await HotkeyChecks.RunAsync(smokeDirectory);
                var timer = Stopwatch.StartNew();
                var smokeSettings = Path.Combine(smokeDirectory, "settings-v2.json");
                if (File.Exists(smokeSettings)) File.Delete(smokeSettings);
                var store = new ProfileStore(smokeSettings, Path.Combine(smokeDirectory, "settings.json"));
                await store.LoadAsync();
                var smokeProfile = store.GetActiveProfile();
                smokeProfile.Application.Theme = ApplicationTheme.Dark;
                smokeProfile.Application.MinimizeToTray = false;
                await store.UpdateActiveAsync(smokeProfile);
                var recentRecording = Path.Combine(smokeDirectory, "recent-demo.mp4");
                await File.WriteAllBytesAsync(recentRecording, []);
                await store.AddRecentFileAsync(recentRecording);
                var main = new MainWindow(store, previewMode: true);
                NativeDesktop.Exclude(main);
                if (!NativeDesktop.IsExcluded(main)) throw new InvalidOperationException("Main window capture exclusion failed.");
                var mainDpi = VisualTreeHelper.GetDpi(main).DpiScaleX * 96;
                if (Math.Abs(mainDpi - NativeDesktop.DpiForWindow(main)) > 1)
                    throw new InvalidOperationException("WPF and Win32 disagree about the main-window DPI.");
                if (!main.CheckTrayLifecycleForSmoke()) throw new InvalidOperationException("Notification-area lifecycle failed.");
                main.RecentFilesButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (main.RecentFilesButton.ContextMenu?.Items[0] is not MenuItem { Tag: string recentPath, IsEnabled: true } ||
                    !string.Equals(recentPath, recentRecording, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("The recent-recording menu did not expose the saved file.");
                main.RecentFilesButton.ContextMenu.IsOpen = false;
                await RenderAsync(main, Path.Combine(smokeDirectory, "main.png"), 414, 722);
                var mainReadyMs = timer.ElapsedMilliseconds;
                var applicationSettings = new ApplicationSettingsWindow(store.GetActiveProfile().Application, store.GetActiveProfile().Selection);
                NativeDesktop.Exclude(applicationSettings);
                applicationSettings.AlwaysOnTopCheckBox.IsChecked = false;
                applicationSettings.CloseToTrayCheckBox.IsChecked = true;
                applicationSettings.ThemeComboBox.SelectedIndex = 1;
                applicationSettings.SelectionHandleSizeSlider.Value = 22;
                applicationSettings.SelectionHandleShapeCombo.SelectedIndex = 1;
                if (!applicationSettings.TryApply() || applicationSettings.Result is not
                    { AlwaysOnTop: false, MinimizeToTray: true, Theme: ApplicationTheme.Light } ||
                    applicationSettings.ResultSelection is not { HandleSize: 22, HandleShape: SelectionHandleShape.Square })
                    throw new InvalidOperationException("Application behavior settings were not applied.");
                await RenderAsync(applicationSettings, Path.Combine(smokeDirectory, "application-settings.png"), 444, 372);
                await ExpandAsync(applicationSettings.SelectionAppearanceExpander);
                await RenderAsync(applicationSettings, Path.Combine(smokeDirectory, "application-settings-selection.png"), 444, 760);
                var captureSettings = new CaptureSettingsWindow(store.GetActiveProfile().Capture);
                captureSettings.AutomaticFpsCheck.IsChecked = false;
                captureSettings.RecordingFpsCombo.SelectedItem = null;
                captureSettings.RecordingFpsCombo.Text = "invalid";
                if (captureSettings.TryApply() || captureSettings.Result.RecordingFps != 30)
                    throw new InvalidOperationException("Invalid advanced capture FPS changed the profile.");
                captureSettings.RecordingFpsCombo.Text = "23.976";
                captureSettings.CountdownCombo.SelectedItem = null;
                captureSettings.CountdownCombo.Text = "7";
                captureSettings.DurationCombo.SelectedItem = null;
                captureSettings.DurationCombo.Text = "123";
                captureSettings.LockAspectCheck.IsChecked = true;
                captureSettings.AspectWidthBox.Text = "21";
                captureSettings.AspectHeightBox.Text = "9";
                captureSettings.SnapCheck.IsChecked = false;
                captureSettings.MinimumSizeCombo.SelectedItem = null;
                captureSettings.MinimumSizeCombo.Text = "64";
                if (!captureSettings.TryApply() || captureSettings.Result is not
                    { AutomaticFps: false, RecordingFps: 23.976, CountdownSeconds: 7, MaximumDurationSeconds: 123,
                      LockAspectRatio: true, AspectWidth: 21, AspectHeight: 9, SnapToEdges: false, RegionMinimumSize: 64 })
                    throw new InvalidOperationException("Exact advanced capture settings were not applied.");
                await RenderAsync(captureSettings, Path.Combine(smokeDirectory, "capture-settings.png"), 504, 612);
                captureSettings.Close();
                ApplicationThemeManager.Apply(ApplicationTheme.Light);
                if (Resources["TextBrush"] is not SolidColorBrush { Color: var lightText } || lightText != Color.FromRgb(0x17, 0x19, 0x1E))
                    throw new InvalidOperationException("The light application palette was not applied.");
                await RenderAsync(main, Path.Combine(smokeDirectory, "main-light.png"), 414, 722);
                ApplicationThemeManager.Apply(ApplicationTheme.Dark);
                applicationSettings.Close();
                main.FormatComboBox.SelectedIndex = 1;
                await RenderAsync(main, Path.Combine(smokeDirectory, "main-gif.png"), 414, 722);
                main.FormatComboBox.SelectedIndex = 0;
                var fallback = await EncoderFallback.PrepareAsync(
                    hardware => hardware ? Task.FromException<int>(new InvalidOperationException("hardware unavailable")) : Task.FromResult(7),
                    value => value == 7 ? null : "unexpected result");
                if (!fallback.UsedSoftware || fallback.Value != 7) throw new InvalidOperationException("Software encoder fallback was not selected.");
                var editorWindow = new DesktopWindowInfo(new nint(101), 1001, "Example Document — Editor", "editor", "EditorWindow",
                    new PixelRect(80, 60, 1280, 720), false);
                var minimizedWindow = new DesktopWindowInfo(new nint(102), 1002, "Reference — Browser", "browser", "BrowserWindow",
                    new PixelRect(120, 90, 1100, 760), true);
                var windowSelector = new WindowSelectorWindow([editorWindow, minimizedWindow], null, useLiveWindows: false);
                windowSelector.SearchBox.Text = "browser";
                windowSelector.WindowList.SelectedItem = minimizedWindow;
                if (windowSelector.TryAcceptForChecks()) throw new InvalidOperationException("A minimized window was accepted.");
                windowSelector.SearchBox.Text = "editor";
                if (windowSelector.VisibleWindowCount != 1) throw new InvalidOperationException("Window search returned the wrong results.");
                windowSelector.WindowList.SelectedItem = editorWindow;
                if (!windowSelector.TryAcceptForChecks() || windowSelector.Result != editorWindow)
                    throw new InvalidOperationException("Window selection did not preserve the chosen window.");
                await RenderAsync(windowSelector, Path.Combine(smokeDirectory, "window-selector.png"), 634, 522);
                windowSelector.Close();
                main.SetSelectedWindowForChecks(editorWindow);
                await RenderAsync(main, Path.Combine(smokeDirectory, "main-window.png"), 414, 722);
                var gifProfile = store.GetActiveProfile();
                var mp4Settings = new Mp4SettingsWindow(gifProfile, 1920, 1080);
                mp4Settings.ResolutionCombo.SelectedItem = null;
                mp4Settings.ResolutionCombo.Text = "invalid";
                if (mp4Settings.TryApply() || mp4Settings.Result.Mp4Width != gifProfile.Output.Mp4Width)
                    throw new InvalidOperationException("Invalid MP4 width changed the profile.");
                mp4Settings.ResolutionCombo.Text = "1280";
                if (!mp4Settings.TryApply() || mp4Settings.Result.Mp4Width != 1280)
                    throw new InvalidOperationException("Custom MP4 width was not applied.");
                mp4Settings.FilenameBox.Text = "";
                if (mp4Settings.TryApply()) throw new InvalidOperationException("An empty MP4 filename template was accepted.");
                mp4Settings.FilenameBox.Text = "Demo_{unknown}";
                if (mp4Settings.TryApply()) throw new InvalidOperationException("An unknown MP4 filename placeholder was accepted.");
                mp4Settings.FilenameBox.Text = "Demo_{date}_{time}_{counter}";
                mp4Settings.OpenFolderCheck.IsChecked = true;
                if (!mp4Settings.TryApply() || !mp4Settings.Result.OpenFolderAfterSave)
                    throw new InvalidOperationException("MP4 file behavior settings were not applied.");
                await RenderAsync(mp4Settings, Path.Combine(smokeDirectory, "mp4-settings.png"), 504, 510);
                mp4Settings.Close();
                var recovery = new RecordingFailureWindow(new RecordingFailureException("The H.264 encoder could not start.",
                    new InvalidOperationException("test"), Path.Combine(smokeDirectory, ".recording-test.partial.mp4"), true));
                NativeDesktop.Exclude(recovery);
                await RenderAsync(recovery, Path.Combine(smokeDirectory, "recording-recovery.png"), 594, 462);
                recovery.Close();
                var gifSettings = new GifSettingsWindow(gifProfile, 1280, 720);
                if (!gifSettings.TryApply()) throw new InvalidOperationException("GIF presets could not be applied.");
                gifSettings.WidthCombo.SelectedItem = null;
                gifSettings.WidthCombo.Text = "invalid";
                if (gifSettings.TryApply() || gifSettings.Result.Width != gifProfile.Output.Width)
                    throw new InvalidOperationException("Invalid GIF width changed the profile.");
                gifSettings.WidthCombo.Text = "960";
                gifSettings.GifFpsCombo.SelectedItem = null;
                gifSettings.GifFpsCombo.Text = "23.976";
                if (!gifSettings.TryApply() || gifSettings.GifFps != 23.976 || gifProfile.Capture.GifFps != 12)
                    throw new InvalidOperationException("Custom GIF FPS or isolated settings editing failed.");
                await RenderAsync(gifSettings, Path.Combine(smokeDirectory, "gif-settings.png"), 504, 552);
                await ExpandAsync(gifSettings.AdvancedExpander);
                await RenderAsync(gifSettings, Path.Combine(smokeDirectory, "gif-settings-advanced.png"), 504, 840);
                gifSettings.Close();
                var shortcutEditor = new HotkeyEditorWindow(store.GetActiveProfile().Capture, _ => Task.FromResult<string?>(null));
                shortcutEditor.BeginCapture(RecorderCommand.ToggleRecording);
                if (!shortcutEditor.Assign(RecorderCommand.ToggleRecording, 0x52, KeyModifiers.Control | KeyModifiers.Alt))
                    throw new InvalidOperationException("Shortcut editor rejected a valid captured key.");
                await RenderAsync(shortcutEditor, Path.Combine(smokeDirectory, "shortcuts.png"), 514, 430);
                shortcutEditor.Close();
                var overlay = store.GetActiveProfile().Overlays;
                overlay.Keystrokes.Enabled = true;
                var editor = new OverlayEditorWindow(overlay, 1280, 720, highlightClicks: true);
                NativeDesktop.Exclude(editor);
                editor.PreparePreview();
                await RenderAsync(editor, Path.Combine(smokeDirectory, "overlays.png"), 904, 612);
                var originalInlineText = editor.Result.Label.Lines[0].Text;
                if (!editor.BeginInlineEditForChecks(0)) throw new InvalidOperationException("Inline label editing did not start.");
                editor.SetInlineTextForChecks("This edit must be cancelled");
                await RenderAsync(editor, Path.Combine(smokeDirectory, "overlays-inline-edit.png"), 904, 612);
                editor.CancelInlineEditForChecks();
                if (editor.Result.Label.Lines[0].Text != originalInlineText)
                    throw new InvalidOperationException("Cancelling inline label editing changed the text.");
                if (!editor.BeginInlineEditForChecks(0)) throw new InvalidOperationException("Inline label editing could not restart.");
                editor.SetInlineTextForChecks("Edited directly on canvas");
                editor.CommitInlineEditForChecks();
                if (editor.Result.Label.Lines[0].Text != "Edited directly on canvas")
                    throw new InvalidOperationException("Inline label editing did not update the inspector model.");
                await editor.ExpandFirstTextStyleAsync();
                await RenderAsync(editor, Path.Combine(smokeDirectory, "overlays-text-style.png"), 904, 612);
                await ExpandAsync(editor.AdvancedLabelExpander);
                var previousBackground = editor.Result.Label.BackgroundColor;
                editor.LabelBackgroundColorBox.Text = "invalid";
                if (editor.Result.Label.BackgroundColor != previousBackground)
                    throw new InvalidOperationException("An invalid advanced label color changed the model.");
                editor.LabelBackgroundColorBox.Text = "#22446688";
                if (editor.Result.Label.BackgroundColor != "#22446688" || editor.Result.Label.Style != LabelStylePreset.Custom)
                    throw new InvalidOperationException("A valid advanced label color was not applied as a custom style.");
                editor.AdvancedLabelExpander.BringIntoView();
                await RenderAsync(editor, Path.Combine(smokeDirectory, "overlays-advanced.png"), 904, 780);
                editor.KeystrokesTab.IsSelected = true;
                await RenderAsync(editor, Path.Combine(smokeDirectory, "overlays-keys.png"), 904, 612);
                await ExpandAsync(editor.AdvancedKeystrokeExpander);
                await RenderAsync(editor, Path.Combine(smokeDirectory, "overlays-keys-advanced.png"), 904, 700);
                editor.ClicksTab.IsSelected = true;
                editor.ClickSizeSlider.Value = 58;
                if (!editor.HighlightClicks || editor.Result.Clicks.Size != 58)
                    throw new InvalidOperationException("Mouse-click settings were not applied.");
                await RenderAsync(editor, Path.Combine(smokeDirectory, "overlays-clicks.png"), 904, 612);
                await ExpandAsync(editor.AdvancedClickExpander);
                await RenderAsync(editor, Path.Combine(smokeDirectory, "overlays-clicks-advanced.png"), 904, 700);
                var portrait = new OverlayEditorWindow(overlay, 360, 640);
                NativeDesktop.Exclude(portrait);
                portrait.PreparePreview();
                await RenderAsync(portrait, Path.Combine(smokeDirectory, "overlays-portrait.png"), 904, 612);
                portrait.Close();
                var display = NativeDesktop.Displays().First();
                using (var boundary = new RegionBoundary(display, new PixelRect(100, 100, 640, 360), "#7B61FFFF", 2))
                {
                    if (!boundary.IsVisible || !boundary.HasExpectedBounds || !boundary.IsPassive || !boundary.IsExcluded)
                        throw new InvalidOperationException("The boundary is not visible, passive, correctly positioned or excluded from capture.");
                }
                var liveProfile = store.GetActiveProfile();
                liveProfile.Overlays.Desktop.ShowLabel = true;
                liveProfile.Overlays.Desktop.ShowKeystrokes = true;
                var liveBounds = new PixelRect(display.Bounds.X + 100, display.Bounds.Y + 100, 640, 360);
                using (var liveOverlay = new DesktopOverlayWindow(liveBounds, liveProfile.Overlays, liveProfile.Capture))
                {
                    if (!liveOverlay.IsVisible || !liveOverlay.HasExpectedBounds || !liveOverlay.IsPassive ||
                        !liveOverlay.IsExcludedFromCapture || liveOverlay.HasCaptureSizedSurface || liveOverlay.VisibleSurfaceCount == 0)
                        throw new InvalidOperationException($"The live desktop overlay failed verification: visible={liveOverlay.IsVisible}, " +
                            $"bounds={liveOverlay.HasExpectedBounds}, passive={liveOverlay.IsPassive}, excluded={liveOverlay.IsExcludedFromCapture}, " +
                            $"captureSized={liveOverlay.HasCaptureSizedSurface}, visibleSurfaces={liveOverlay.VisibleSurfaceCount}.");
                }
                var selectorProfile = store.GetActiveProfile();
                selectorProfile.Selection.HandleShape = SelectionHandleShape.Square;
                selectorProfile.Selection.HandleSize = 22;
                var selector = new RegionSelectorWindow(display, selectorProfile.Capture, selectorProfile.Selection);
                NativeDesktop.Exclude(selector);
                await RenderAsync(selector, Path.Combine(smokeDirectory, "region.png"), 1100, 620);
                if (NativeDesktop.WindowBounds(selector) != display.Bounds)
                    throw new InvalidOperationException("The region selector did not retain exact physical display bounds.");
                var selectorDpi = VisualTreeHelper.GetDpi(selector).DpiScaleX * 96;
                if (Math.Abs(selectorDpi - NativeDesktop.DpiForWindow(selector)) > 1)
                    throw new InvalidOperationException("WPF and Win32 disagree about the region-selector DPI.");
                selector.Close(); editor.Close(); main.Close();
                await File.WriteAllTextAsync(Path.Combine(smokeDirectory, "result.txt"),
                    $"PASS: Per-Monitor V2/WPF-Win32 DPI agreement on every connected display, exact physical selector placement, WPF main window, recent-recording menu, light/dark theme resources, application behavior, selection appearance and advanced capture settings, notification-area lifecycle, searchable window selector, window-source summary, MP4 preset/custom validation, encoder fallback/recovery UI, GIF preset/custom validation and layout, inline/advanced overlay editor, label background blur and per-row shadows, mouse-click appearance and animation, shortcut assignment, Win32 hotkey registration/conflict/cleanup, stale-message rejection, countdown command routing, overlay rendering, region selector, visible passive boundary and live desktop overlay placement, profile persistence and transfer.\nDisplays: {dpiReport}.\nMain layout ready: {mainReadyMs} ms (in-process smoke check, not cold launch).\n");
                Shutdown(0);
                return;
            }

            if (startupProbeDirectory is not null)
            {
                Directory.CreateDirectory(startupProbeDirectory);
                var probeStore = new ProfileStore(Path.Combine(startupProbeDirectory, "settings-v2.json"),
                    Path.Combine(startupProbeDirectory, "legacy-settings.json"));
                await probeStore.LoadAsync();
                ApplicationThemeManager.Apply(probeStore.GetActiveProfile().Application.Theme);
                var rendered = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
                var probe = new MainWindow(probeStore, previewMode: true)
                {
                    ShowActivated = false,
                    ShowInTaskbar = false,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = -32000,
                    Top = -32000,
                };
                probe.ContentRendered += (_, _) => rendered.TrySetResult(Stopwatch.GetTimestamp());
                NativeDesktop.Exclude(probe);
                MainWindow = probe;
                probe.Show();
                var renderedTimestamp = await rendered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                var report = new
                {
                    ProcessId = Environment.ProcessId,
                    StopwatchFrequency = Stopwatch.Frequency,
                    RenderedTimestamp = renderedTimestamp,
                    OnStartupToContentRenderedMilliseconds = startupTimer.Elapsed.TotalMilliseconds,
                    ExecutableBytes = new FileInfo(Environment.ProcessPath!).Length,
                };
                await File.WriteAllTextAsync(Path.Combine(startupProbeDirectory, "result.json"),
                    JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
                probe.Close();
                Shutdown(0);
                return;
            }

            instanceMutex = new Mutex(true, @"Local\ScreenDemoRecorder.Native", out ownsMutex);
            if (!ownsMutex)
            {
                MessageBox.Show("Screen Demo Recorder is already running.", "Screen Demo Recorder");
                Shutdown(0);
                return;
            }
            var profiles = new ProfileStore();
            await profiles.LoadAsync();
            ApplicationThemeManager.Apply(profiles.GetActiveProfile().Application.Theme);
            var window = new MainWindow(profiles);
            MainWindow = window;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            window.Show();
        }
        catch (Exception error)
        {
            if (cpuPipelineCheckDirectory is not null)
                await File.WriteAllTextAsync(Path.Combine(cpuPipelineCheckDirectory, "failure.txt"), error.ToString());
            else if (recordingCheckDirectory is not null)
                await File.WriteAllTextAsync(Path.Combine(recordingCheckDirectory, "failure.txt"), error.ToString());
            else if (smokeDirectory is not null)
                await File.WriteAllTextAsync(Path.Combine(smokeDirectory, "failure.txt"), error.ToString());
            else if (startupProbeDirectory is not null)
            {
                Directory.CreateDirectory(startupProbeDirectory);
                await File.WriteAllTextAsync(Path.Combine(startupProbeDirectory, "failure.txt"), error.ToString());
            }
            else
                MessageBox.Show(error.Message, "Screen Demo Recorder Cannot Start", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private static async Task ExpandAsync(Expander expander)
    {
        var factor = expander.Template.FindName("AnimationFactorBorder", expander) as FrameworkElement;
        if (factor is null) { expander.IsExpanded = true; return; }
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var descriptor = DependencyPropertyDescriptor.FromProperty(FrameworkElement.WidthProperty, factor.GetType());
        EventHandler changed = (_, _) => { if (factor.Width == 0) completed.TrySetResult(); };
        descriptor.AddValueChanged(factor, changed);
        try
        {
            expander.IsExpanded = true;
            await expander.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            if (factor.Width == 0) completed.TrySetResult();
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(3));
        }
        finally { descriptor.RemoveValueChanged(factor, changed); }
    }

    private static string CheckConnectedDisplayDpi()
    {
        var reports = new List<string>();
        foreach (var display in NativeDesktop.Displays())
        {
            var probe = new Window
            {
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                ShowActivated = false,
            };
            try
            {
                var bounds = new PixelRect(display.Bounds.X + 8, display.Bounds.Y + 8,
                    Math.Min(64, display.Bounds.Width), Math.Min(64, display.Bounds.Height));
                NativeDesktop.Place(probe, bounds, true);
                if (!NativeDesktop.IsExcluded(probe) || NativeDesktop.WindowBounds(probe) != bounds)
                    throw new InvalidOperationException($"The DPI probe was not placed or excluded on {display.DeviceName}.");
                var nativeDpi = NativeDesktop.DpiForWindow(probe);
                var wpfDpi = VisualTreeHelper.GetDpi(probe).DpiScaleX * 96;
                if (Math.Abs(nativeDpi - wpfDpi) > 1)
                    throw new InvalidOperationException($"WPF and Win32 disagree about DPI on {display.DeviceName}.");
                reports.Add($"{display.DeviceName} {display.Bounds.Width}x{display.Bounds.Height} at {nativeDpi * 100 / 96}%");
            }
            finally { probe.Close(); }
        }
        if (reports.Count == 0) throw new InvalidOperationException("Windows reported no connected displays.");
        return string.Join(", ", reports);
    }

    private static async Task RenderAsync(Window window, string path, int width, int height)
    {
        var content = (FrameworkElement)window.Content;
        if (content is Panel panel) panel.Background = window.Background;
        content.Measure(new Size(width, height));
        content.Arrange(new Rect(0, 0, width, height));
        content.UpdateLayout();
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        content.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
    }
}
