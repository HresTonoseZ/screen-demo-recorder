using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using ScreenDemoRecorder.Core.Models;
using ScreenDemoRecorder.Core.Services;

namespace ScreenDemoRecorder;

public partial class GifSettingsWindow : Window
{
    private readonly int sourceWidth;
    private readonly int sourceHeight;
    private bool ready;
    public OutputSettings Result { get; private set; }
    public double GifFps { get; private set; }

    public GifSettingsWindow(RecorderProfile profile, int captureWidth, int captureHeight)
    {
        Result = JsonSerializer.Deserialize<OutputSettings>(JsonSerializer.Serialize(profile.Output))!;
        GifFps = profile.Capture.GifFps;
        sourceWidth = captureWidth; sourceHeight = captureHeight;
        InitializeComponent();
        Select(WidthCombo, Result.Width);
        Select(GifFpsCombo, GifFps);
        Select(PaletteCombo, Result.GifPaletteColors);
        Select(LoopCombo, Result.GifLoopCount);
        Select(StepCombo, Result.GifFrameStep);
        Select(HoldCombo, Result.FinalFrameDurationMilliseconds);
        DitherCheck.IsChecked = Result.GifDither;
        KeepSourceCheck.IsChecked = Result.KeepSourceVideo;
        FilenameBox.Text = Result.FilenameTemplate;
        OpenFolderCheck.IsChecked = Result.OpenFolderAfterSave;
        SourceInitialized += (_, _) => NativeDesktop.Exclude(this);
        ready = true;
        foreach (var combo in new[] { WidthCombo, GifFpsCombo, StepCombo })
            combo.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => UpdateSummary()));
        UpdateSummary();
    }

    private void MatchCapture_Click(object sender, RoutedEventArgs e) { Select(WidthCombo, Math.Clamp(sourceWidth, 64, 7680)); UpdateSummary(); }
    private void SettingChanged(object sender, RoutedEventArgs e) { if (ready) UpdateSummary(); }

    private void UpdateSummary()
    {
        try
        {
            var width = Integer(WidthCombo, "Width", 64, 7680);
            var fps = Number(GifFpsCombo, "GIF frame rate", 1, 60);
            var step = Integer(StepCombo, "Frame sampling", 1, 30);
            var height = Math.Max(1, Math.Round(sourceHeight * (double)width / sourceWidth));
            SizeSummary.Text = $"{width} × {height} px · {fps / step:g4} fps · aspect ratio preserved" +
                (width > sourceWidth ? "\nLarger than the capture; this will not add detail." : "") +
                (fps / step > 50 ? "\nAbove 50 fps, some GIF viewers slow the animation down." : "");
        }
        catch (ArgumentException error) { SizeSummary.Text = error.Message; }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (TryApply()) DialogResult = true;
    }

    internal bool TryApply()
    {
        try
        {
            var output = JsonSerializer.Deserialize<OutputSettings>(JsonSerializer.Serialize(Result))!;
            var fps = Number(GifFpsCombo, "GIF frame rate", 1, 60);
            output.Width = Integer(WidthCombo, "Width", 64, 7680);
            output.GifPaletteColors = Integer(PaletteCombo, "Colors", 2, 256);
            output.GifLoopCount = Integer(LoopCombo, "Repeat", 0, 10_000);
            output.GifFrameStep = Integer(StepCombo, "Frame sampling", 1, 30);
            output.FinalFrameDurationMilliseconds = Integer(HoldCombo, "Last-frame duration", 0, 60_000);
            output.GifDither = DitherCheck.IsChecked == true;
            output.KeepSourceVideo = KeepSourceCheck.IsChecked == true;
            output.OpenFolderAfterSave = OpenFolderCheck.IsChecked == true;
            output.FilenameTemplate = RecordingOutput.ValidateFilenameTemplate(FilenameBox.Text);
            _ = new GifExportPlan(sourceWidth, sourceHeight, TimeSpan.FromSeconds(1), new CaptureSettings { GifFps = fps }, output);
            Result = output; GifFps = fps;
            UpdateSummary();
            ErrorText.Visibility = Visibility.Collapsed;
            return true;
        }
        catch (ArgumentException error)
        {
            ErrorText.Text = error.Message;
            ErrorText.Visibility = Visibility.Visible;
            return false;
        }
    }

    private static void Select(ComboBox combo, double value)
    {
        var tag = value.ToString("G", CultureInfo.InvariantCulture);
        combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().FirstOrDefault(item => item.Tag?.ToString() == tag);
        if (combo.SelectedItem is null) combo.Text = tag;
    }

    private static double Number(ComboBox combo, string name, double minimum, double maximum)
    {
        var text = combo.SelectedItem is ComboBoxItem item && combo.Text == item.Content?.ToString() ? item.Tag?.ToString() : combo.Text;
        if ((!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) &&
             !double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value)) || !double.IsFinite(value) || value < minimum || value > maximum)
            throw new ArgumentException($"{name}: choose a preset or enter a number from {minimum} to {maximum}.");
        return value;
    }

    private static int Integer(ComboBox combo, string name, int minimum, int maximum)
    {
        var value = Number(combo, name, minimum, maximum);
        if (value != Math.Truncate(value)) throw new ArgumentException($"{name}: enter a whole number.");
        return (int)value;
    }
}
