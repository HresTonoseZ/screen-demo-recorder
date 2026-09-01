using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using ScreenDemoRecorder.Core.Models;

namespace ScreenDemoRecorder;

public partial class CaptureSettingsWindow : Window
{
    public CaptureSettingsWindow(CaptureSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Result = JsonSerializer.Deserialize<CaptureSettings>(JsonSerializer.Serialize(settings))!;
        InitializeComponent();
        AutomaticFpsCheck.IsChecked = Result.AutomaticFps;
        Select(RecordingFpsCombo, Result.RecordingFps);
        Select(CountdownCombo, Result.CountdownSeconds);
        Select(DurationCombo, Result.MaximumDurationSeconds);
        LockAspectCheck.IsChecked = Result.LockAspectRatio;
        AspectWidthBox.Text = Result.AspectWidth.ToString(CultureInfo.InvariantCulture);
        AspectHeightBox.Text = Result.AspectHeight.ToString(CultureInfo.InvariantCulture);
        SnapCheck.IsChecked = Result.SnapToEdges;
        Select(MinimumSizeCombo, Result.RegionMinimumSize);
        RecordingFpsCombo.IsEnabled = !Result.AutomaticFps;
        SourceInitialized += (_, _) => NativeDesktop.Exclude(this);
    }

    public CaptureSettings Result { get; private set; }

    private void AutomaticFpsChanged(object sender, RoutedEventArgs e)
    {
        if (RecordingFpsCombo is not null) RecordingFpsCombo.IsEnabled = AutomaticFpsCheck.IsChecked != true;
    }

    private void AspectPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string ratio }) return;
        var parts = ratio.Split(':');
        AspectWidthBox.Text = parts[0];
        AspectHeightBox.Text = parts[1];
        LockAspectCheck.IsChecked = true;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (TryApply()) DialogResult = true;
    }

    internal bool TryApply()
    {
        try
        {
            var updated = JsonSerializer.Deserialize<CaptureSettings>(JsonSerializer.Serialize(Result))!;
            updated.AutomaticFps = AutomaticFpsCheck.IsChecked == true;
            updated.RecordingFps = Number(RecordingFpsCombo, "Recording FPS", 1, 120);
            updated.CountdownSeconds = Integer(CountdownCombo, "Countdown", 0, 10);
            updated.MaximumDurationSeconds = Integer(DurationCombo, "Maximum duration", 0, 86_400);
            updated.LockAspectRatio = LockAspectCheck.IsChecked == true;
            updated.AspectWidth = Integer(AspectWidthBox.Text, "Aspect width", 1, 1000);
            updated.AspectHeight = Integer(AspectHeightBox.Text, "Aspect height", 1, 1000);
            updated.SnapToEdges = SnapCheck.IsChecked == true;
            updated.RegionMinimumSize = Integer(MinimumSizeCombo, "Minimum region size", 16, 1000);
            Result = updated;
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
        var text = combo.SelectedItem is ComboBoxItem item && combo.Text == item.Content?.ToString()
            ? item.Tag?.ToString() ?? string.Empty
            : combo.Text;
        if ((!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) &&
             !double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value)) ||
            !double.IsFinite(value) || value < minimum || value > maximum)
            throw new ArgumentException($"{name}: choose a preset or enter a number from {minimum} to {maximum}.");
        return value;
    }

    private static int Integer(ComboBox combo, string name, int minimum, int maximum)
    {
        var value = Number(combo, name, minimum, maximum);
        if (value != Math.Truncate(value)) throw new ArgumentException($"{name}: enter a whole number.");
        return (int)value;
    }

    private static int Integer(string text, string name, int minimum, int maximum)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var value) || value < minimum || value > maximum)
            throw new ArgumentException($"{name}: enter a whole number from {minimum} to {maximum}.");
        return value;
    }
}
