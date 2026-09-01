using System.Windows;
using System.Windows.Controls;
using ScreenDemoRecorder.Core.Models;

namespace ScreenDemoRecorder;

public partial class ApplicationSettingsWindow : Window
{
    public ApplicationSettingsWindow(ApplicationSettings settings, SelectionSettings? selection = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        InitializeComponent();
        Result = new ApplicationSettings
        {
            AlwaysOnTop = settings.AlwaysOnTop,
            MinimizeToTray = settings.MinimizeToTray,
            Theme = settings.Theme,
        };
        ResultSelection = CloneSelection(selection ?? new SelectionSettings());
        AlwaysOnTopCheckBox.IsChecked = Result.AlwaysOnTop;
        CloseToTrayCheckBox.IsChecked = Result.MinimizeToTray;
        SelectTheme(Result.Theme);
        WriteSelectionControls();
    }

    public ApplicationSettings Result { get; }

    public SelectionSettings ResultSelection { get; }

    internal bool TryApply()
    {
        if (!Enum.TryParse<ApplicationTheme>((ThemeComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString(), out var theme))
            return false;
        if (!TryColor(SelectionColorBox.Text, out var selectionColor) || !TryColor(RecordingColorBox.Text, out var recordingColor) ||
            !TryColor(DimColorBox.Text, out var dimColor) || !TryColor(HandleColorBox.Text, out var handleColor) ||
            !TryColor(HandleBorderColorBox.Text, out var handleBorder) || !TryColor(DimensionColorBox.Text, out var dimensionColor) ||
            !Enum.TryParse<SelectionHandleShape>((SelectionHandleShapeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString(), out var handleShape))
        {
            ErrorText.Text = "Check the exact colors and handle shape in Area selection appearance.";
            return false;
        }
        ErrorText.Text = string.Empty;
        Result.AlwaysOnTop = AlwaysOnTopCheckBox.IsChecked == true;
        Result.MinimizeToTray = CloseToTrayCheckBox.IsChecked == true;
        Result.Theme = theme;
        ResultSelection.SelectionColor = selectionColor;
        ResultSelection.RecordingColor = recordingColor;
        ResultSelection.DimColor = dimColor;
        ResultSelection.HandleColor = handleColor;
        ResultSelection.HandleBorderColor = handleBorder;
        ResultSelection.DimensionColor = dimensionColor;
        ResultSelection.LineWidth = (int)Math.Round(SelectionLineWidthSlider.Value);
        ResultSelection.DashLength = (int)Math.Round(SelectionDashLengthSlider.Value);
        ResultSelection.DashGap = (int)Math.Round(SelectionDashGapSlider.Value);
        ResultSelection.HandleSize = (int)Math.Round(SelectionHandleSizeSlider.Value);
        ResultSelection.HandleBorderWidth = (int)Math.Round(SelectionHandleBorderSlider.Value);
        ResultSelection.HandleShape = handleShape;
        ResultSelection.ShowDimensions = ShowDimensionsCheckBox.IsChecked == true;
        ResultSelection.DimensionSize = (int)Math.Round(SelectionDimensionSizeSlider.Value);
        return true;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (TryApply()) DialogResult = true;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e) => NativeDesktop.Exclude(this);

    private void SelectTheme(ApplicationTheme theme)
    {
        ThemeComboBox.SelectedItem = ThemeComboBox.Items.OfType<ComboBoxItem>()
            .First(item => string.Equals(item.Tag?.ToString(), theme.ToString(), StringComparison.OrdinalIgnoreCase));
    }

    private void WriteSelectionControls()
    {
        SelectionColorBox.Text = ResultSelection.SelectionColor;
        RecordingColorBox.Text = ResultSelection.RecordingColor;
        DimColorBox.Text = ResultSelection.DimColor;
        HandleColorBox.Text = ResultSelection.HandleColor;
        HandleBorderColorBox.Text = ResultSelection.HandleBorderColor;
        DimensionColorBox.Text = ResultSelection.DimensionColor;
        SelectionLineWidthSlider.Value = ResultSelection.LineWidth;
        SelectionDashLengthSlider.Value = ResultSelection.DashLength;
        SelectionDashGapSlider.Value = ResultSelection.DashGap;
        SelectionHandleSizeSlider.Value = ResultSelection.HandleSize;
        SelectionHandleBorderSlider.Value = ResultSelection.HandleBorderWidth;
        SelectionHandleShapeCombo.SelectedItem = SelectionHandleShapeCombo.Items.OfType<ComboBoxItem>()
            .First(item => item.Tag?.ToString() == ResultSelection.HandleShape.ToString());
        ShowDimensionsCheckBox.IsChecked = ResultSelection.ShowDimensions;
        SelectionDimensionSizeSlider.Value = ResultSelection.DimensionSize;
    }

    private void SelectionPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string preset }) return;
        (SelectionColorBox.Text, RecordingColorBox.Text, DimColorBox.Text, HandleColorBox.Text, HandleBorderColorBox.Text, DimensionColorBox.Text) = preset switch
        {
            "Blue" => ("#4C97FFFF", "#EE4B5FFF", "#00000099", "#FFFFFFFF", "#2F70EEFF", "#FFFFFFFF"),
            "Contrast" => ("#FFFFFFFF", "#FFFF00FF", "#000000B8", "#FFFF00FF", "#000000FF", "#FFFFFFFF"),
            _ => ("#7B61FFFF", "#EE4B5FFF", "#00000080", "#FFFFFFFF", "#6A48E8FF", "#FFFFFFFF"),
        };
    }

    private static bool TryColor(string value, out string normalized)
    {
        normalized = value.Trim().ToUpperInvariant();
        return normalized.StartsWith('#') && normalized.Length is 7 or 9 && normalized[1..].All(Uri.IsHexDigit);
    }

    private static SelectionSettings CloneSelection(SelectionSettings source) => new()
    {
        SelectionColor = source.SelectionColor,
        RecordingColor = source.RecordingColor,
        LineWidth = source.LineWidth,
        DashLength = source.DashLength,
        DashGap = source.DashGap,
        HandleColor = source.HandleColor,
        HandleBorderColor = source.HandleBorderColor,
        HandleBorderWidth = source.HandleBorderWidth,
        HandleSize = source.HandleSize,
        HandleShape = source.HandleShape,
        DimColor = source.DimColor,
        ShowDimensions = source.ShowDimensions,
        DimensionColor = source.DimensionColor,
        DimensionSize = source.DimensionSize,
        KeepBoundaryVisible = source.KeepBoundaryVisible,
    };
}
