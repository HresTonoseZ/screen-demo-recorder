using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using ScreenDemoRecorder.Core.Models;
using ScreenDemoRecorder.Core.Services;

namespace ScreenDemoRecorder;

public partial class Mp4SettingsWindow : Window
{
    private readonly int sourceWidth;
    private readonly int sourceHeight;
    private bool ready;
    public OutputSettings Result { get; private set; }

    public Mp4SettingsWindow(RecorderProfile profile, int captureWidth, int captureHeight)
    {
        Result = JsonSerializer.Deserialize<OutputSettings>(JsonSerializer.Serialize(profile.Output))!;
        sourceWidth = captureWidth;
        sourceHeight = captureHeight;
        InitializeComponent();
        Select(Result.Mp4Width);
        FilenameBox.Text = Result.FilenameTemplate;
        OpenFolderCheck.IsChecked = Result.OpenFolderAfterSave;
        SourceInitialized += (_, _) => NativeDesktop.Exclude(this);
        ready = true;
        ResolutionCombo.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => UpdateSummary()));
        UpdateSummary();
    }

    private void SettingChanged(object sender, RoutedEventArgs e) { if (ready) UpdateSummary(); }

    private void UpdateSummary()
    {
        try
        {
            var requested = ReadWidth();
            var plan = Mp4OutputPlan.Create(sourceWidth, sourceHeight, requested);
            SizeSummary.Text = $"{plan.ContentWidth} × {plan.ContentHeight} px" +
                (plan.Width != plan.ContentWidth || plan.Height != plan.ContentHeight ? $" · encoded as {plan.Width} × {plan.Height}" : "") +
                (requested > sourceWidth ? "\nThe capture is smaller, so it will not be enlarged." : "") +
                (plan.IsResized ? "\nHigh-quality GPU scaling is applied before encoding." : "\nOriginal capture detail is retained.");
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
            output.Mp4Width = ReadWidth();
            output.FilenameTemplate = RecordingOutput.ValidateFilenameTemplate(FilenameBox.Text);
            output.OpenFolderAfterSave = OpenFolderCheck.IsChecked == true;
            _ = Mp4OutputPlan.Create(sourceWidth, sourceHeight, output.Mp4Width);
            Result = output;
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

    private void Select(int value)
    {
        var tag = value.ToString(CultureInfo.InvariantCulture);
        ResolutionCombo.SelectedItem = ResolutionCombo.Items.OfType<ComboBoxItem>().FirstOrDefault(item => item.Tag?.ToString() == tag);
        if (ResolutionCombo.SelectedItem is null) ResolutionCombo.Text = tag;
    }

    private int ReadWidth()
    {
        var text = ResolutionCombo.SelectedItem is ComboBoxItem item && ResolutionCombo.Text == item.Content?.ToString()
            ? item.Tag?.ToString()
            : ResolutionCombo.Text;
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) &&
            !int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out value))
            throw new ArgumentException("Resolution: choose Original, a preset, or enter a whole number from 64 to 7680.");
        if (value != 0 && value is < 64 or > 7680)
            throw new ArgumentException("Resolution: choose Original, a preset, or enter a whole number from 64 to 7680.");
        return value;
    }
}
