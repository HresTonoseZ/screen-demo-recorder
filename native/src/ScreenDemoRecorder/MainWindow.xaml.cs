using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Microsoft.VisualBasic;
using Microsoft.Win32;
using ScreenDemoRecorder.Capture;
using ScreenDemoRecorder.Core.Models;
using ScreenDemoRecorder.Core.Services;

namespace ScreenDemoRecorder;

public partial class MainWindow : Window
{
    private readonly ProfileStore store;
    private RecorderProfile profile = new();
    private CancellationTokenSource? pendingSave;
    private bool loading = true;
    private bool closeAllowed;
    private bool profileOperation;
    private RegionBoundary? boundary;
    private DesktopOverlayWindow? desktopOverlay;
    private string? liveOverlayWarning;
    private IReadOnlyList<DisplayInfo> displays = [];
    private DesktopWindowInfo? selectedWindow;
    internal bool PreviewMode { get; }

    public MainWindow(ProfileStore profileStore, bool previewMode = false)
    {
        store = profileStore;
        PreviewMode = previewMode;
        InitializeComponent();
        displays = NativeDesktop.Displays();
        DisplayComboBox.ItemsSource = displays;
        RefreshProfileList();
        LoadActiveProfile();
        StatusText.Text = "Saved · native preview build";
        if (!previewMode)
        {
            SourceInitialized += (_, _) => NativeDesktop.Exclude(this);
            ContentRendered += (_, _) => RefreshBoundary();
            Activated += MainWindow_Activated;
            SystemEvents.DisplaySettingsChanged += DisplaysChanged;
            InitializeHotkeys();
            InitializeTray();
        }
    }

    private void DisplaysChanged(object? sender, EventArgs e)
    {
        Dispatcher.InvokeAsync(() =>
        {
            var windowSession = recordingBusy && profile.Capture.Source == CaptureSource.Window;
            if (recordingBusy && !windowSession)
            {
                countdown?.Cancel();
                recording?.Stop();
                StatusText.Text = "Display configuration changed. Finishing the recording…";
            }
            boundary?.Dispose(); boundary = null;
            desktopOverlay?.Dispose(); desktopOverlay = null;
            loading = true;
            displays = NativeDesktop.Displays();
            DisplayComboBox.ItemsSource = displays;
            if (windowSession)
            {
                loading = false;
                return;
            }
            LoadActiveProfile();
            RefreshBoundary();
        });
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        if (!closeAllowed && !PreviewMode && !exitRequested && profile.Application.MinimizeToTray && trayIcon is not null)
        {
            e.Cancel = true;
            if (profileOperation) return;
            IsEnabled = false;
            try { await TryCloseToTrayAsync(); }
            catch (Exception error) { ShowError(error, "Cannot Close to Notification Area"); }
            finally { IsEnabled = true; }
            return;
        }
        if (!closeAllowed && !PreviewMode)
        {
            e.Cancel = true;
            if (profileOperation) return;
            IsEnabled = false;
            closingAfterRecording = true;
            try
            {
                hotkeys?.Clear();
                countdown?.Cancel();
                exportCancellation?.Cancel();
                recording?.Stop();
                if (recordingTask is not null) await recordingTask;
                await SaveNowAsync(); closeAllowed = true; _ = Dispatcher.InvokeAsync(Close);
            }
            catch (Exception error) { closingAfterRecording = false; IsEnabled = true; ConfigureHotkeys(); ShowError(error, "Cannot Save Before Closing"); }
            return;
        }
        pendingSave?.Cancel(); pendingSave?.Dispose();
        boundary?.Dispose(); boundary = null;
        desktopOverlay?.Dispose(); desktopOverlay = null;
        hotkeys?.Dispose(); hotkeys = null;
        DisposeTray();
        SystemEvents.DisplaySettingsChanged -= DisplaysChanged;
        base.OnClosing(e);
    }

    private void RefreshProfileList()
    {
        loading = true;
        ProfileComboBox.ItemsSource = store.ProfileNames;
        ProfileComboBox.SelectedItem = store.ActiveProfileName;
        loading = false;
    }

    private void LoadActiveProfile()
    {
        loading = true;
        profile = store.GetActiveProfile();
        selectedWindow = ResolveSavedWindow(profile.Capture);

        DisplayComboBox.SelectedItem = profile.Capture.DisplayDeviceName is { } device
            ? displays.FirstOrDefault(d => d.DeviceName == device)
            : displays.FirstOrDefault(d => d.Index == profile.Capture.DisplayIndex);

        RegionSource.IsChecked = profile.Capture.Source == CaptureSource.Region;
        DisplaySource.IsChecked = profile.Capture.Source == CaptureSource.Display;
        WindowSource.IsChecked = profile.Capture.Source == CaptureSource.Window;
        SelectComboItem(FpsComboBox, profile.Capture.AutomaticFps ? "0" : profile.Capture.RecordingFps.ToString(System.Globalization.CultureInfo.InvariantCulture));
        SelectComboItem(CountdownComboBox, profile.Capture.CountdownSeconds.ToString());
        SelectComboItem(DurationComboBox, profile.Capture.MaximumDurationSeconds.ToString());
        SelectComboItem(FormatComboBox, profile.Output.Format.ToString());
        SelectComboItem(QualityComboBox, profile.Output.Quality.ToString());
        OutputDirectoryTextBox.Text = profile.Output.Directory;
        CursorCheckBox.IsChecked = profile.Capture.ShowCursor;
        LabelCheckBox.IsChecked = profile.Overlays.Label.Enabled;
        KeystrokesCheckBox.IsChecked = profile.Overlays.Keystrokes.Enabled;
        ClicksCheckBox.IsChecked = profile.Capture.HighlightClicks;
        BoundaryCheckBox.IsChecked = profile.Selection.KeepBoundaryVisible;
        DesktopLabelCheckBox.IsChecked = profile.Overlays.Desktop.ShowLabel;
        DesktopKeystrokesCheckBox.IsChecked = profile.Overlays.Desktop.ShowKeystrokes;
        DesktopClicksCheckBox.IsChecked = profile.Overlays.Desktop.ShowMouseClicks;
        ApplicationThemeManager.Apply(profile.Application.Theme);
        Topmost = profile.Application.AlwaysOnTop;
        UpdateTrayState();
        UpdateSourceControls();
        UpdateAreaSummary();
        UpdateRecordLabel();
        loading = false;
        ConfigureHotkeys();
        if (IsLoaded) RefreshBoundary();
    }

    private void UpdateProfileFromControls()
    {
        profile.Capture.Source = RegionSource.IsChecked == true
            ? CaptureSource.Region
            : DisplaySource.IsChecked == true
                ? CaptureSource.Display
                : CaptureSource.Window;

        var fps = double.TryParse(SelectedTag(FpsComboBox), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var rate) ? rate : 30;
        profile.Capture.AutomaticFps = fps == 0;
        if (fps > 0)
        {
            profile.Capture.RecordingFps = fps;
        }

        profile.Capture.CountdownSeconds = SelectedInteger(CountdownComboBox, 3);
        profile.Capture.MaximumDurationSeconds = SelectedInteger(DurationComboBox, 60);
        profile.Output.Format = Enum.TryParse<OutputFormat>(SelectedTag(FormatComboBox), out var format)
            ? format
            : OutputFormat.Mp4;
        profile.Output.Quality = Enum.TryParse<QualityPreset>(SelectedTag(QualityComboBox), out var quality)
            ? quality
            : QualityPreset.Balanced;
        profile.Output.Directory = OutputDirectoryTextBox.Text;
        profile.Capture.ShowCursor = CursorCheckBox.IsChecked == true;
        profile.Overlays.Label.Enabled = LabelCheckBox.IsChecked == true;
        profile.Overlays.Keystrokes.Enabled = KeystrokesCheckBox.IsChecked == true;
        profile.Capture.HighlightClicks = ClicksCheckBox.IsChecked == true;
        profile.Selection.KeepBoundaryVisible = BoundaryCheckBox.IsChecked == true;
        profile.Overlays.Desktop.ShowLabel = DesktopLabelCheckBox.IsChecked == true;
        profile.Overlays.Desktop.ShowKeystrokes = DesktopKeystrokesCheckBox.IsChecked == true;
        profile.Overlays.Desktop.ShowMouseClicks = DesktopClicksCheckBox.IsChecked == true;
        UpdateRecordLabel();
    }

    private void CaptureSource_Checked(object sender, RoutedEventArgs e)
    {
        if (loading || recordingBusy)
        {
            return;
        }

        UpdateSourceControls();
        UpdateAreaSummary();
        ScheduleSave();
        RefreshBoundary();
    }

    private void CommonSetting_Changed(object sender, RoutedEventArgs e)
    {
        ScheduleSave();
    }

    private void ScheduleSave()
    {
        if (loading)
        {
            return;
        }

        UpdateProfileFromControls();
        pendingSave?.Cancel();
        pendingSave?.Dispose();
        pendingSave = new CancellationTokenSource();
        _ = SaveAfterDelayAsync(pendingSave.Token);
    }

    private async Task SaveAfterDelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            StatusText.Text = "Saving…";
            await Task.Delay(350, cancellationToken);
            await store.UpdateAsync(store.ActiveProfileName, profile, cancellationToken);
            StatusText.Text = "Saved";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            StatusText.Text = "Save failed";
            MessageBox.Show(this, error.Message, "Cannot Save Profile", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task SaveNowAsync()
    {
        pendingSave?.Cancel();
        pendingSave?.Dispose();
        pendingSave = null;
        UpdateProfileFromControls();
        await store.UpdateAsync(store.ActiveProfileName, profile);
        StatusText.Text = "Saved";
    }

    private async void ProfileComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (loading || profileOperation || ProfileComboBox.SelectedItem is not string selected || selected == store.ActiveProfileName)
        {
            return;
        }

        try
        {
            profileOperation = true; IsEnabled = false;
            await SaveNowAsync();
            await store.ActivateAsync(selected);
            LoadActiveProfile();
            StatusText.Text = $"Profile: {selected}";
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Cannot Switch Profile", MessageBoxButton.OK, MessageBoxImage.Error);
            RefreshProfileList();
        }
        finally { profileOperation = false; IsEnabled = true; }
    }

    private void ProfileMenuButton_Click(object sender, RoutedEventArgs e)
    {
        if (ProfileMenuButton.ContextMenu is null)
        {
            return;
        }

        ProfileMenuButton.ContextMenu.PlacementTarget = ProfileMenuButton;
        ProfileMenuButton.ContextMenu.IsOpen = true;
    }

    private async void DuplicateProfile_Click(object sender, RoutedEventArgs e)
    {
        if (profileOperation) return;
        profileOperation = true; IsEnabled = false;
        try
        {
            await SaveNowAsync();
            await store.DuplicateAsync($"{store.ActiveProfileName} copy");
            RefreshProfileList();
            LoadActiveProfile();
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Cannot Duplicate Profile", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { profileOperation = false; IsEnabled = true; }
    }

    private async void RenameProfile_Click(object sender, RoutedEventArgs e)
    {
        if (profileOperation) return;
        string name;
        profileOperation = true;
        try { name = Interaction.InputBox("Enter a new profile name.", "Rename Profile", store.ActiveProfileName).Trim(); }
        finally { profileOperation = false; }
        if (name.Length == 0 || name == store.ActiveProfileName)
        {
            return;
        }

        profileOperation = true; IsEnabled = false;
        try
        {
            await SaveNowAsync();
            await store.RenameActiveAsync(name);
            RefreshProfileList();
            LoadActiveProfile();
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Cannot Rename Profile", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { profileOperation = false; IsEnabled = true; }
    }

    private async void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (profileOperation) return;
        if (MessageBox.Show(
                this,
                $"Delete profile '{store.ActiveProfileName}'?",
                "Delete Profile",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        profileOperation = true; IsEnabled = false;
        try
        {
            await SaveNowAsync();
            await store.DeleteActiveAsync();
            RefreshProfileList();
            LoadActiveProfile();
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Cannot Delete Profile", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { profileOperation = false; IsEnabled = true; }
    }

    private async void ResetProfile_Click(object sender, RoutedEventArgs e)
    {
        if (profileOperation || MessageBox.Show(this, $"Reset every setting in profile '{store.ActiveProfileName}'?",
                "Reset Profile", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        profileOperation = true; IsEnabled = false;
        try
        {
            await store.ResetActiveAsync();
            LoadActiveProfile();
            StatusText.Text = "Profile reset";
        }
        catch (Exception error) { ShowError(error, "Cannot Reset Profile"); }
        finally { profileOperation = false; IsEnabled = true; }
    }

    private async void ExportProfile_Click(object sender, RoutedEventArgs e)
    {
        if (profileOperation) return;
        var dialog = new SaveFileDialog
        {
            Title = "Export Profile",
            Filter = "Screen Demo Recorder profile (*.json)|*.json",
            DefaultExt = ".json",
            AddExtension = true,
            FileName = SafeProfileFileName(store.ActiveProfileName) + ".json",
        };
        if (dialog.ShowDialog(this) != true) return;
        profileOperation = true; IsEnabled = false;
        try
        {
            await SaveNowAsync();
            await store.ExportActiveAsync(dialog.FileName);
            StatusText.Text = $"Exported {Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception error) { ShowError(error, "Cannot Export Profile"); }
        finally { profileOperation = false; IsEnabled = true; }
    }

    private async void ImportProfile_Click(object sender, RoutedEventArgs e)
    {
        if (profileOperation) return;
        var dialog = new OpenFileDialog
        {
            Title = "Import Profile",
            Filter = "Screen Demo Recorder profile (*.json)|*.json|JSON files (*.json)|*.json",
            CheckFileExists = true,
            Multiselect = false,
        };
        if (dialog.ShowDialog(this) != true) return;
        profileOperation = true; IsEnabled = false;
        try
        {
            await SaveNowAsync();
            var imported = await store.ImportAsync(dialog.FileName);
            RefreshProfileList();
            LoadActiveProfile();
            StatusText.Text = $"Imported profile: {imported}";
        }
        catch (Exception error) { ShowError(error, "Cannot Import Profile"); }
        finally { profileOperation = false; IsEnabled = true; }
    }

    private async void ApplicationSettings_Click(object sender, RoutedEventArgs e)
    {
        if (profileOperation || recordingBusy) return;
        profileOperation = true; IsEnabled = false;
        try
        {
            await SaveNowAsync();
            var editor = new ApplicationSettingsWindow(profile.Application, profile.Selection) { Owner = this };
            if (editor.ShowDialog() != true) return;
            profile.Application = editor.Result;
            profile.Selection = editor.ResultSelection;
            await SaveNowAsync();
            LoadActiveProfile();
            StatusText.Text = "Application settings saved";
        }
        catch (Exception error) { ShowError(error, "Cannot Save Application Settings"); }
        finally { profileOperation = false; IsEnabled = true; }
    }

    private async void CaptureSettings_Click(object sender, RoutedEventArgs e)
    {
        if (profileOperation || recordingBusy) return;
        profileOperation = true; IsEnabled = false;
        try
        {
            await SaveNowAsync();
            var editor = new CaptureSettingsWindow(store.GetActiveProfile().Capture) { Owner = this };
            if (editor.ShowDialog() != true) return;
            var updated = store.GetActiveProfile();
            updated.Capture = editor.Result;
            await store.UpdateActiveAsync(updated);
            LoadActiveProfile();
            StatusText.Text = "Advanced capture settings saved";
        }
        catch (Exception error) { ShowError(error, "Cannot Save Capture Settings"); }
        finally { profileOperation = false; IsEnabled = true; }
    }

    private static string SafeProfileFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(name.Select(character => invalid.Contains(character) ? '_' : character).ToArray()).Trim();
        return clean.Length == 0 ? "Profile" : clean;
    }

    private void BrowseOutput_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose Recording Folder",
            InitialDirectory = OutputDirectoryTextBox.Text,
        };
        if (dialog.ShowDialog(this) == true)
        {
            OutputDirectoryTextBox.Text = dialog.FolderName;
        }
    }

    private void RecentFiles_Click(object sender, RoutedEventArgs e)
    {
        if (RecentFilesButton.ContextMenu is not { } menu) return;
        menu.Items.Clear();
        foreach (var path in store.RecentFiles)
        {
            var exists = File.Exists(path);
            var folder = Path.GetFileName(Path.GetDirectoryName(path));
            var item = new MenuItem
            {
                Header = $"{Path.GetFileName(path)}{(string.IsNullOrWhiteSpace(folder) ? "" : $" — {folder}")}{(exists ? "" : " (missing)")}",
                ToolTip = path,
                Tag = path,
                IsEnabled = exists,
            };
            item.Click += RecentPath_Click;
            menu.Items.Add(item);
        }
        if (menu.Items.Count == 0)
            menu.Items.Add(new MenuItem { Header = "No recordings yet", IsEnabled = false });

        menu.Items.Add(new Separator());
        string? outputFolder;
        try
        {
            outputFolder = Path.GetFullPath(string.IsNullOrWhiteSpace(OutputDirectoryTextBox.Text)
                ? Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)
                : Environment.ExpandEnvironmentVariables(OutputDirectoryTextBox.Text.Trim()));
        }
        catch { outputFolder = null; }
        var openFolder = new MenuItem
        {
            Header = "Open save folder",
            ToolTip = outputFolder ?? "Choose a valid save folder first.",
            Tag = outputFolder ?? string.Empty,
            IsEnabled = outputFolder is not null && Directory.Exists(outputFolder),
        };
        openFolder.Click += RecentPath_Click;
        menu.Items.Add(openFolder);
        menu.PlacementTarget = RecentFilesButton;
        menu.IsOpen = true;
    }

    private void RecentPath_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string path }) return;
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception error) { ShowError(error, "Cannot Open Recording"); }
    }

    private async void EditOverlays_Click(object sender, RoutedEventArgs e)
    {
        if (profileOperation || recordingBusy) return;
        profileOperation = true; IsEnabled = false;
        try
        {
            await SaveNowAsync();
            var target = CaptureTargetFactory.Create(profile.Capture, displays, selectedWindow);
            var editor = new OverlayEditorWindow(profile.Overlays, target.Area.Width, target.Area.Height, profile.Capture.HighlightClicks) { Owner = this };
            if (editor.ShowDialog() == true)
            {
                profile.Overlays = editor.Result;
                profile.Capture.HighlightClicks = editor.HighlightClicks;
                loading = true;
                LabelCheckBox.IsChecked = profile.Overlays.Label.Enabled;
                KeystrokesCheckBox.IsChecked = profile.Overlays.Keystrokes.Enabled;
                ClicksCheckBox.IsChecked = profile.Capture.HighlightClicks;
                loading = false;
                await SaveNowAsync();
                RefreshBoundary();
            }
        }
        catch (Exception error) { ShowError(error, "Cannot Save Overlays"); }
        finally { profileOperation = false; IsEnabled = true; }
    }

    private async void SelectRegion_Click(object sender, RoutedEventArgs e)
    {
        if (WindowSource.IsChecked == true)
        {
            try
            {
                var selector = new WindowSelectorWindow(selectedWindow) { Owner = this };
                if (selector.ShowDialog() == true && selector.Result is { } window)
                {
                    selectedWindow = window;
                    profile.Capture.Source = CaptureSource.Window;
                    profile.Capture.WindowTitle = window.Title;
                    profile.Capture.WindowProcessName = window.ProcessName;
                    profile.Capture.WindowClassName = window.ClassName;
                    loading = true; WindowSource.IsChecked = true; loading = false;
                    UpdateSourceControls();
                    UpdateAreaSummary();
                    await SaveNowAsync();
                    RefreshBoundary();
                }
            }
            catch (Exception error) { ShowError(error, "Window Selection Failed"); }
            return;
        }
        if (DisplayComboBox.SelectedItem is not DisplayInfo display) return;
        boundary?.Dispose(); boundary = null;
        desktopOverlay?.Dispose(); desktopOverlay = null;
        try
        {
            var selector = new RegionSelectorWindow(display, profile.Capture, profile.Selection);
            Hide();
            if (selector.ShowDialog() == true)
            {
                profile.Capture.Source = CaptureSource.Region;
                profile.Capture.Region = selector.SelectedRegion.ToSavedRegion();
                profile.Capture.LockAspectRatio = selector.LockAspectRatio;
                profile.Capture.SnapToEdges = selector.SnapToEdges;
                profile.Capture.DisplayDeviceName = display.DeviceName;
                profile.Capture.DisplayIndex = display.Index;
                loading = true; RegionSource.IsChecked = true; loading = false;
                UpdateAreaSummary();
                await SaveNowAsync();
            }
        }
        catch (Exception error) { ShowError(error, "Region Selection Failed"); }
        finally { Show(); Activate(); RefreshBoundary(); }
    }

    private void DisplayComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (loading || DisplayComboBox.SelectedItem is not DisplayInfo display) return;
        profile.Capture.DisplayIndex = display.Index;
        profile.Capture.DisplayDeviceName = display.DeviceName;
        UpdateAreaSummary(); ScheduleSave(); RefreshBoundary();
    }

    private void BoundarySetting_Changed(object sender, RoutedEventArgs e)
    {
        if (loading) return;
        ScheduleSave(); RefreshBoundary();
    }

    private void DesktopOverlaySetting_Changed(object sender, RoutedEventArgs e)
    {
        if (loading) return;
        ScheduleSave();
        RefreshBoundary();
    }

    private void RefreshBoundary()
    {
        if (recordingBusy && recording is null) return;
        boundary?.Dispose(); boundary = null;
        desktopOverlay?.Dispose(); desktopOverlay = null;
        liveOverlayWarning = null;
        if (PreviewMode || !TryGetDesktopOverlayBounds(out var bounds)) return;

        try
        {
            var live = profile.Overlays.Desktop;
            if (live.ShowLabel || live.ShowKeystrokes || live.ShowMouseClicks)
                desktopOverlay = new DesktopOverlayWindow(bounds, profile.Overlays, profile.Capture);

            if (BoundaryCheckBox.IsChecked == true && WindowSource.IsChecked != true)
                boundary = new RegionBoundary(bounds, profile.Selection.SelectionColor, profile.Selection.LineWidth);
        }
        catch (Exception error)
        {
            boundary?.Dispose(); boundary = null;
            desktopOverlay?.Dispose(); desktopOverlay = null;
            liveOverlayWarning = $"Live overlay disabled because Windows could not verify capture exclusion: {error.Message}";
            StatusText.Text = liveOverlayWarning;
        }
    }

    private bool TryGetDesktopOverlayBounds(out PixelRect bounds)
    {
        bounds = default;
        if (WindowSource.IsChecked == true)
        {
            if (!RefreshSelectedWindow() || selectedWindow!.IsMinimized) return false;
            bounds = selectedWindow.Bounds;
            return true;
        }

        if (DisplayComboBox.SelectedItem is not DisplayInfo display) return false;
        if (DisplaySource.IsChecked == true)
        {
            bounds = display.Bounds;
            return true;
        }

        if (RegionSource.IsChecked != true || profile.Capture.Region is not { } region) return false;
        var area = new PixelRect(region.X, region.Y, region.Width, region.Height);
        if (RegionGeometry.Fit(area, display.Bounds.Width, display.Bounds.Height, profile.Capture.RegionMinimumSize) != area)
        {
            StatusText.Text = "Saved region no longer fits. Select a new area.";
            return false;
        }

        bounds = new PixelRect(display.Bounds.X + area.X, display.Bounds.Y + area.Y, area.Width, area.Height);
        return true;
    }

    private void ShowError(Exception error, string title)
    {
        MessageBox.Show(this, error.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void UpdateAreaSummary()
    {
        if (WindowSource.IsChecked == true)
        {
            SelectRegionButton.IsEnabled = true;
            if (selectedWindow is null)
            {
                AreaSummaryText.Text = "Choose a window before recording";
            }
            else if (!RefreshSelectedWindow())
            {
                AreaSummaryText.Text = selectedWindow.IsMinimized ? "Restore the selected window" : "Selected window is no longer open";
            }
            else
            {
                AreaSummaryText.Text = $"{selectedWindow!.Title} · {selectedWindow.Bounds.Width} × {selectedWindow.Bounds.Height}";
            }
            UpdateRecordLabel();
            return;
        }
        SelectRegionButton.IsEnabled = DisplayComboBox.SelectedItem is DisplayInfo;
        if (DisplayComboBox.SelectedItem is null)
        {
            AreaSummaryText.Text = "Saved display is disconnected"; UpdateRecordLabel(); return;
        }
        if (RegionSource.IsChecked == true)
        {
            AreaSummaryText.Text = profile.Capture.Region is { } region
                ? $"{region.Width} × {region.Height} · Display {profile.Capture.DisplayIndex}"
                : "Select a region";
        }
        else if (DisplaySource.IsChecked == true)
        {
            AreaSummaryText.Text = $"Entire display {profile.Capture.DisplayIndex}";
        }
        UpdateRecordLabel();
    }

    private void UpdateRecordLabel()
    {
        var gif = profile.Output.Format == OutputFormat.Gif;
        GifSettingsButton.Visibility = gif ? Visibility.Visible : Visibility.Collapsed;
        Mp4SettingsButton.Visibility = gif ? Visibility.Collapsed : Visibility.Visible;
        Mp4SettingsButton.Content = profile.Output.Mp4Width == 0 ? "Original" : $"{profile.Output.Mp4Width}px";
        Mp4SettingsButton.ToolTip = profile.Output.Mp4Width == 0 ? "MP4 resolution · match capture" : $"MP4 resolution · {profile.Output.Mp4Width} pixels wide";
        QualityLabel.Visibility = QualityComboBox.Visibility = gif ? Visibility.Collapsed : Visibility.Visible;
        GifSettingsButton.ToolTip = $"{profile.Output.Width} px wide · {profile.Capture.GifFps:g} fps · {profile.Output.GifPaletteColors} colors";
        if (recordingBusy) return;
        var seconds = SelectedInteger(CountdownComboBox, profile.Capture.CountdownSeconds);
        var format = gif ? "GIF" : "MP4";
        RecordButton.Content = seconds == 0 ? $"Record {format}" : $"Record {format} ({seconds}s)";
        var sourceReady = WindowSource.IsChecked == true
            ? RefreshSelectedWindow()
            : DisplayComboBox.SelectedItem is DisplayInfo &&
                (DisplaySource.IsChecked == true || RegionSource.IsChecked == true && profile.Capture.Region is not null);
        RecordButton.IsEnabled = sourceReady;
        RecordButton.ToolTip = !sourceReady ? WindowSource.IsChecked == true
                ? "Choose an open, restored window first."
                : "Select a capture area on a connected display first."
            : profile.Overlays.Label.Enabled ? "Record the selected area with your label" : "Record the selected area without a label";
    }

    private async void GifSettings_Click(object sender, RoutedEventArgs e)
    {
        if (profileOperation || recordingBusy) return;
        profileOperation = true; IsEnabled = false;
        try
        {
            await SaveNowAsync();
            var target = CaptureTargetFactory.Create(profile.Capture, displays, selectedWindow);
            var editor = new GifSettingsWindow(profile, target.Area.Width, target.Area.Height) { Owner = this };
            if (editor.ShowDialog() != true) return;
            var updated = store.GetActiveProfile();
            updated.Output = editor.Result;
            updated.Capture.GifFps = editor.GifFps;
            await store.UpdateActiveAsync(updated);
            LoadActiveProfile();
            StatusText.Text = "GIF settings saved";
        }
        catch (Exception error) { ShowError(error, "Cannot Save GIF Settings"); }
        finally { profileOperation = false; IsEnabled = true; }
    }

    private async void Mp4Settings_Click(object sender, RoutedEventArgs e)
    {
        if (profileOperation || recordingBusy) return;
        profileOperation = true; IsEnabled = false;
        try
        {
            await SaveNowAsync();
            var target = CaptureTargetFactory.Create(profile.Capture, displays, selectedWindow);
            var editor = new Mp4SettingsWindow(profile, target.Area.Width, target.Area.Height) { Owner = this };
            if (editor.ShowDialog() != true) return;
            var updated = store.GetActiveProfile();
            updated.Output = editor.Result;
            await store.UpdateActiveAsync(updated);
            LoadActiveProfile();
            StatusText.Text = "MP4 settings saved";
        }
        catch (Exception error) { ShowError(error, "Cannot Save MP4 Settings"); }
        finally { profileOperation = false; IsEnabled = true; }
    }

    private static void SelectComboItem(ComboBox comboBox, string tag)
    {
        foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
            {
                comboBox.SelectedItem = item;
                return;
            }
        }

        var custom = new ComboBoxItem { Content = $"Custom ({tag})", Tag = tag };
        comboBox.Items.Add(custom);
        comboBox.SelectedItem = custom;
    }

    private static string SelectedTag(ComboBox comboBox)
    {
        return (comboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? string.Empty;
    }

    private static int SelectedInteger(ComboBox comboBox, int fallback)
    {
        return int.TryParse(SelectedTag(comboBox), out var value) ? value : fallback;
    }

    private void UpdateSourceControls()
    {
        var window = WindowSource.IsChecked == true;
        DisplayComboBox.Visibility = window ? Visibility.Collapsed : Visibility.Visible;
        BoundaryCheckBox.Visibility = window ? Visibility.Collapsed : Visibility.Visible;
        SelectRegionButton.Content = window ? "Choose" : "Select";
    }

    private DesktopWindowInfo? ResolveSavedWindow(CaptureSettings capture)
    {
        if (capture.WindowTitle is null || capture.WindowProcessName is null || capture.WindowClassName is null) return null;
        var matches = NativeDesktop.Windows().Where(window =>
            window.Matches(capture.WindowTitle, capture.WindowProcessName, capture.WindowClassName)).Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private bool RefreshSelectedWindow()
    {
        if (selectedWindow is null) return false;
        if (PreviewMode) return !selectedWindow.IsMinimized;
        if (!NativeDesktop.TryGetWindow(selectedWindow.Handle, out var current) || current.ProcessId != selectedWindow.ProcessId ||
            !string.Equals(current.ClassName, selectedWindow.ClassName, StringComparison.Ordinal))
        {
            return false;
        }
        selectedWindow = current;
        return !current.IsMinimized;
    }

    private void MainWindow_Activated(object? sender, EventArgs e)
    {
        if (loading || recordingBusy || profileOperation || WindowSource.IsChecked != true) return;
        UpdateAreaSummary();
    }

    internal void SetSelectedWindowForChecks(DesktopWindowInfo window)
    {
        selectedWindow = window;
        profile.Capture.Source = CaptureSource.Window;
        profile.Capture.WindowTitle = window.Title;
        profile.Capture.WindowProcessName = window.ProcessName;
        profile.Capture.WindowClassName = window.ClassName;
        loading = true;
        WindowSource.IsChecked = true;
        loading = false;
        UpdateSourceControls();
        UpdateAreaSummary();
    }
}
