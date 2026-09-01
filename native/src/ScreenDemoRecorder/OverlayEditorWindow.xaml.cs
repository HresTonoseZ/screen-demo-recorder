using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ScreenDemoRecorder.Core.Models;
using ScreenDemoRecorder.Core.Services;
using ScreenDemoRecorder.Overlays;

namespace ScreenDemoRecorder;

public partial class OverlayEditorWindow : Window
{
    private readonly int frameWidth;
    private readonly int frameHeight;
    private readonly ObservableCollection<LabelTextLine> lines;
    private FrameworkElement? draggedElement;
    private Point dragOffset;
    private bool loading = true;
    private bool privacyWarningShown;
    private LabelRaster? renderedLabel;
    private bool previewQueued;
    private Point resizeStart;
    private int resizeWidth;
    private PixelRect dragContainer;
    private LabelTextLine? editingLine;
    private string originalInlineText = string.Empty;
    private bool inlineUpdating;
    private PixelRect inlineBounds;

    public OverlayEditorWindow(OverlaySettings overlays, int captureWidth, int captureHeight, bool highlightClicks = false)
    {
        frameWidth = captureWidth;
        frameHeight = captureHeight;
        InitializeComponent();
        PreviewCanvas.Width = frameWidth;
        PreviewCanvas.Height = frameHeight;
        Result = Clone(overlays);
        HighlightClicks = highlightClicks;
        lines = new ObservableCollection<LabelTextLine>(Result.Label.Lines);
        LabelLinesEditor.ItemsSource = lines;
    }

    public OverlaySettings Result { get; private set; }

    public bool HighlightClicks { get; private set; }

    internal async Task ExpandFirstTextStyleAsync()
    {
        var expander = FindVisualChildren<Expander>(LabelLinesEditor).First();
        var factor = expander.Template.FindName("AnimationFactorBorder", expander) as FrameworkElement;
        if (factor is null) { expander.IsExpanded = true; return; }
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var descriptor = DependencyPropertyDescriptor.FromProperty(WidthProperty, factor.GetType());
        EventHandler changed = (_, _) => { if (factor.Width == 0) completed.TrySetResult(); };
        descriptor.AddValueChanged(factor, changed);
        try
        {
            expander.IsExpanded = true;
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            if (factor.Width == 0) completed.TrySetResult();
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(3));
        }
        finally { descriptor.RemoveValueChanged(factor, changed); }
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        NativeDesktop.Exclude(this);
        PreparePreview();
    }

    internal void PreparePreview()
    {
        LabelEnabledCheckBox.IsChecked = Result.Label.Enabled;
        SelectComboItem(LabelStyleComboBox, Result.Label.Style.ToString());
        LabelWidthSlider.Minimum = Math.Min(80, frameWidth);
        LabelWidthSlider.Maximum = frameWidth;
        LabelWidthSlider.Value = Math.Min(Result.Label.Width, frameWidth);
        KeystrokesEnabledCheckBox.IsChecked = Result.Keystrokes.Enabled;
        SelectComboItem(KeystrokeModeComboBox, Result.Keystrokes.DisplayMode.ToString());
        SelectComboItem(KeystrokeStyleComboBox, Result.Keystrokes.Style.ToString());
        KeystrokeScaleSlider.Value = Result.Keystrokes.Scale;
        KeystrokeDurationSlider.Value = Result.Keystrokes.VisibleDurationMilliseconds;
        KeystrokeStackSlider.Value = Result.Keystrokes.MaximumStackEntries;
        KeystrokeOpacitySlider.Value = Result.Keystrokes.Opacity;
        KeystrokeFadeSlider.Value = Result.Keystrokes.FadeDurationMilliseconds;
        KeystrokeMergeWindowSlider.Value = Result.Keystrokes.MergeWindowMilliseconds;
        MergeCombinationsCheckBox.IsChecked = Result.Keystrokes.MergeCombinations;
        HideRecorderHotkeysCheckBox.IsChecked = Result.Keystrokes.HideRecorderHotkeys;
        ClicksEnabledCheckBox.IsChecked = HighlightClicks;
        ClickSizeSlider.Value = Result.Clicks.Size;
        ClickWidthSlider.Value = Result.Clicks.RingWidth;
        ClickDurationSlider.Value = Result.Clicks.DurationMilliseconds;
        ClickOpacitySlider.Value = Result.Clicks.Opacity;
        LeftClickColorBox.Text = Result.Clicks.LeftColor;
        RightClickColorBox.Text = Result.Clicks.RightColor;
        WriteAdvancedLabelControls();
        loading = false;
        LabelLinesEditor.SelectedItem = lines.FirstOrDefault(line => line.Enabled) ?? lines.FirstOrDefault();
        RefreshPreview();
    }

    private void PreviewCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (loading) return;
        UpdateLabelGuides();
    }

    private void RefreshPreview()
    {
        if (loading)
        {
            return;
        }

        Result.Label.Lines = lines.ToList();
        ProfileValidator.Normalize(new RecorderProfile { Overlays = Result });
        renderedLabel = LabelRenderer.Render(Result.Label, frameWidth, frameHeight);
        LabelPreview.Source = renderedLabel?.Bitmap;
        LabelPreview.Visibility = LabelOutline.Visibility = LabelResizeHandle.Visibility = renderedLabel is null ? Visibility.Collapsed : Visibility.Visible;
        if (renderedLabel is { } label)
        {
            LabelPreview.Width = label.Bounds.Width;
            LabelPreview.Height = label.Bounds.Height;
            Canvas.SetLeft(LabelPreview, label.Bounds.X);
            Canvas.SetTop(LabelPreview, label.Bounds.Y);
        }
        PreviewSummary.Text = $"{frameWidth} × {frameHeight} · exact recording layout" +
            (renderedLabel?.TextClipped == true ? "\nText is too tall. Reduce its size or remove rows." : "");
        UpdateLabelGuides();
        UpdateInlineEditor();

        KeystrokePreview.Visibility = Result.Keystrokes.Enabled ? Visibility.Visible : Visibility.Collapsed;
        KeystrokePreview.BeginAnimation(OpacityProperty, null);
        var renderer = new KeystrokeRenderer(Result.Keystrokes, ["Ctrl", "Shift", "S"]);
        var raster = renderer.RenderPreview([new(new KeyChord(["Ctrl", "Shift", "S"]), Result.Keystrokes.Opacity)], frameWidth, frameHeight)!;
        KeystrokePreview.Source = raster.Bitmap;
        KeystrokePreview.Width = raster.Bounds.Width; KeystrokePreview.Height = raster.Bounds.Height;
        Canvas.SetLeft(KeystrokePreview, raster.Bounds.X); Canvas.SetTop(KeystrokePreview, raster.Bounds.Y);
        RefreshClickPreview(MouseClickButton.Left);
    }

    private void LabelSetting_Changed(object sender, RoutedEventArgs e)
    {
        if (loading)
        {
            return;
        }

        Result.Label.Enabled = LabelEnabledCheckBox.IsChecked == true;
        RefreshPreview();
    }

    private void LabelStyleComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (loading || !Enum.TryParse<LabelStylePreset>(SelectedTag(LabelStyleComboBox), out var style))
        {
            return;
        }

        Result.Label.Style = style;
        if (style != LabelStylePreset.Custom) Result.Label.ShadowColor = "#00000070";
        switch (style)
        {
            case LabelStylePreset.Clean:
                Result.Label.BackgroundBlur = 0;
                Result.Label.BackgroundColor = "#F7F8FAF2";
                Result.Label.BorderColor = "#20242C22";
                SetLineColors("#17191EFF", "#626A76FF");
                break;
            case LabelStylePreset.Glass:
                Result.Label.BackgroundBlur = 12;
                Result.Label.BackgroundColor = "#090E18D9";
                Result.Label.BorderColor = "#FFFFFF30";
                SetLineColors("#FFFFFFFF", "#C4D0E4FF");
                break;
            case LabelStylePreset.Accent:
                Result.Label.BackgroundBlur = 0;
                Result.Label.BackgroundColor = "#6A48E8F0";
                Result.Label.BorderColor = "#B8AAFFFF";
                SetLineColors("#FFFFFFFF", "#E8E3FFFF");
                break;
            case LabelStylePreset.Dark:
                Result.Label.BackgroundBlur = 0;
                Result.Label.BackgroundColor = "#101217F2";
                Result.Label.BorderColor = "#FFFFFF18";
                SetLineColors("#FFFFFFFF", "#B8BEC8FF");
                break;
            case LabelStylePreset.TextOnly:
                Result.Label.BackgroundBlur = 0;
                Result.Label.BackgroundColor = "#00000000";
                Result.Label.BorderColor = "#00000000";
                Result.Label.ShadowColor = "#00000000";
                SetLineColors("#FFFFFFFF", "#C4D0E4FF");
                break;
            case LabelStylePreset.Custom:
            default:
                break;
        }

        WriteAdvancedLabelControls();
        RefreshPreview();
    }

    private void SetLineColors(string primary, string secondary)
    {
        for (var index = 0; index < lines.Count; index++)
        {
            lines[index].Color = index == 0 ? primary : secondary;
        }
    }

    private void AddLine_Click(object sender, RoutedEventArgs e)
    {
        var line = new LabelTextLine { Text = "New text row", Size = 16 };
        lines.Add(line);
        LabelLinesEditor.SelectedItem = line;
        RefreshPreview();
    }

    private void RemoveLine_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: LabelTextLine line })
        {
            if (editingLine == line) EndInlineEditing(commit: false);
            var index = lines.IndexOf(line);
            lines.Remove(line);
            if (lines.Count > 0) LabelLinesEditor.SelectedIndex = Math.Min(index, lines.Count - 1);
            RefreshPreview();
        }
    }

    private void LineText_Changed(object sender, TextChangedEventArgs e)
    {
        if (!loading && sender is TextBox { DataContext: LabelTextLine line }) LabelLinesEditor.SelectedItem = line;
        QueuePreview();
    }

    private void LineStyle_Changed(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag?.ToString() is "StrokeWidth" or "ShadowBlur" or "ShadowOffsetX" or "ShadowOffsetY")
            MarkLabelCustom();
        QueuePreview();
    }

    private void QueuePreview()
    {
        if (loading || previewQueued) return;
        previewQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () => { previewQueued = false; RefreshPreview(); });
    }

    private void LineSize_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: LabelTextLine line } button && int.TryParse(button.Tag?.ToString(), out var delta))
        {
            line.Size = Math.Clamp(line.Size + delta, 6, 300);
            RefreshPreview();
        }
    }

    private void MoveLine_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: LabelTextLine line } button || !int.TryParse(button.Tag?.ToString(), out var delta)) return;
        var index = lines.IndexOf(line);
        var destination = Math.Clamp(index + delta, 0, lines.Count - 1);
        if (index != destination) lines.Move(index, destination);
        RefreshPreview();
    }

    private void LineColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: LabelTextLine line, Tag: string color })
        {
            line.Color = color;
            MarkLabelCustom();
            RefreshPreview();
        }
    }

    private void LineExactColor_Changed(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (loading || sender is not TextBox { DataContext: LabelTextLine line, Tag: string property }) return;
        var current = property switch { "StrokeColor" => line.StrokeColor, "ShadowColor" => line.ShadowColor, _ => line.Color };
        if (!TryNormalizeColor(((TextBox)sender).Text, out var color))
        {
            ((TextBox)sender).Text = current;
            PreviewSummary.Text = "Use #RRGGBB or #RRGGBBAA for exact colors.";
            return;
        }

        if (property == "StrokeColor") line.StrokeColor = color;
        else if (property == "ShadowColor") line.ShadowColor = color;
        else line.Color = color;
        MarkLabelCustom();
        RefreshPreview();
    }

    private void AdvancedLabel_Changed(object sender, RoutedEventArgs e)
    {
        if (loading) return;
        if (!TryNormalizeColor(LabelBackgroundColorBox.Text, out var background) ||
            !TryNormalizeColor(LabelBorderColorBox.Text, out var border) ||
            !TryNormalizeColor(LabelShadowColorBox.Text, out var shadow))
        {
            AdvancedLabelError.Text = "Use #RRGGBB or #RRGGBBAA. Changes apply only after all three colors are valid.";
            return;
        }

        AdvancedLabelError.Text = string.Empty;
        Result.Label.BackgroundColor = background;
        Result.Label.BorderColor = border;
        Result.Label.ShadowColor = shadow;
        Result.Label.PaddingX = (int)Math.Round(LabelPaddingXSlider.Value);
        Result.Label.PaddingY = (int)Math.Round(LabelPaddingYSlider.Value);
        Result.Label.LineGap = (int)Math.Round(LabelLineGapSlider.Value);
        Result.Label.CornerRadius = (int)Math.Round(LabelCornerRadiusSlider.Value);
        Result.Label.BorderWidth = (int)Math.Round(LabelBorderWidthSlider.Value);
        Result.Label.BackgroundBlur = (int)Math.Round(LabelBackgroundBlurSlider.Value);
        Result.Label.ShadowBlur = (int)Math.Round(LabelShadowBlurSlider.Value);
        Result.Label.ShadowOffsetX = (int)Math.Round(LabelShadowXSlider.Value);
        Result.Label.ShadowOffsetY = (int)Math.Round(LabelShadowYSlider.Value);
        MarkLabelCustom();
        QueuePreview();
    }

    private void WriteAdvancedLabelControls()
    {
        var wasLoading = loading;
        loading = true;
        LabelBackgroundColorBox.Text = Result.Label.BackgroundColor;
        LabelBorderColorBox.Text = Result.Label.BorderColor;
        LabelShadowColorBox.Text = Result.Label.ShadowColor;
        LabelPaddingXSlider.Value = Result.Label.PaddingX;
        LabelPaddingYSlider.Value = Result.Label.PaddingY;
        LabelLineGapSlider.Value = Result.Label.LineGap;
        LabelCornerRadiusSlider.Value = Result.Label.CornerRadius;
        LabelBorderWidthSlider.Value = Result.Label.BorderWidth;
        LabelBackgroundBlurSlider.Value = Result.Label.BackgroundBlur;
        LabelShadowBlurSlider.Value = Result.Label.ShadowBlur;
        LabelShadowXSlider.Value = Result.Label.ShadowOffsetX;
        LabelShadowYSlider.Value = Result.Label.ShadowOffsetY;
        AdvancedLabelError.Text = string.Empty;
        loading = wasLoading;
    }

    private void MarkLabelCustom()
    {
        if (loading || Result.Label.Style == LabelStylePreset.Custom) return;
        Result.Label.Style = LabelStylePreset.Custom;
        var wasLoading = loading;
        loading = true;
        SelectComboItem(LabelStyleComboBox, nameof(LabelStylePreset.Custom));
        loading = wasLoading;
    }

    private static bool TryNormalizeColor(string value, out string normalized)
    {
        normalized = value.Trim().ToUpperInvariant();
        return normalized.StartsWith('#') && normalized.Length is 7 or 9 && normalized[1..].All(Uri.IsHexDigit);
    }

    private void LabelWidth_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (loading) return;
        Result.Label.Width = (int)Math.Round(LabelWidthSlider.Value);
        QueuePreview();
    }

    private void UpdateLabelGuides()
    {
        if (renderedLabel is not { } label)
        {
            SelectedLineOutline.Visibility = Visibility.Collapsed;
            return;
        }
        var scale = Math.Max(0.01, Math.Min(PreviewViewport.ActualWidth / frameWidth, PreviewViewport.ActualHeight / frameHeight));
        var handleSize = 12 / scale;
        LabelOutline.Width = label.Container.Width; LabelOutline.Height = label.Container.Height;
        LabelOutline.BorderThickness = new Thickness(1 / scale);
        Canvas.SetLeft(LabelOutline, label.Container.X); Canvas.SetTop(LabelOutline, label.Container.Y);
        LabelResizeHandle.Width = LabelResizeHandle.Height = handleSize;
        Canvas.SetLeft(LabelResizeHandle, Math.Max(0, label.Container.Right - handleSize));
        Canvas.SetTop(LabelResizeHandle, label.Container.Y + (label.Container.Height - handleSize) / 2);
        var selected = LabelLinesEditor.SelectedItem as LabelTextLine;
        var line = selected is null ? null : label.Lines.FirstOrDefault(candidate => candidate.Id == selected.Id);
        SelectedLineOutline.Visibility = line is null ? Visibility.Collapsed : Visibility.Visible;
        if (line is not null)
        {
            SelectedLineOutline.Width = line.Bounds.Width;
            SelectedLineOutline.Height = line.Bounds.Height;
            SelectedLineOutline.BorderThickness = new Thickness(1 / scale);
            Canvas.SetLeft(SelectedLineOutline, line.Bounds.X);
            Canvas.SetTop(SelectedLineOutline, line.Bounds.Y);
        }
    }

    private void LabelLinesEditor_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!loading) UpdateLabelGuides();
    }

    private void LineEnabled_Click(object sender, RoutedEventArgs e)
    {
        RefreshPreview();
    }

    private void LabelAnchor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || !Enum.TryParse<OverlayAnchor>(button.Tag?.ToString(), out var anchor))
        {
            return;
        }

        Result.Label.Anchor = anchor;
        Result.Label.OffsetX = 0;
        Result.Label.OffsetY = 0;
        RefreshPreview();
    }

    private void KeystrokeSetting_Changed(object sender, RoutedEventArgs e)
    {
        if (loading)
        {
            return;
        }

        ReadKeystrokeControls();
        RefreshPreview();
    }

    private void KeystrokeModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (loading || !Enum.TryParse<KeystrokeDisplayMode>(SelectedTag(KeystrokeModeComboBox), out var mode))
        {
            return;
        }

        Result.Keystrokes.DisplayMode = mode;
        Result.Keystrokes.HideNormalTyping = mode != KeystrokeDisplayMode.AllKeys;
        if (mode == KeystrokeDisplayMode.AllKeys && !privacyWarningShown)
        {
            privacyWarningShown = true;
            MessageBox.Show(
                this,
                "All-keys mode records ordinary typing and can expose passwords or other sensitive input in the video. Password fields are not detected. Use Shortcuts only or Non-text keys when normal typing must stay private.",
                "Keyboard Privacy",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        RefreshPreview();
    }

    private void KeystrokeStyleComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (loading || !Enum.TryParse<KeystrokeStylePreset>(SelectedTag(KeystrokeStyleComboBox), out var style))
        {
            return;
        }

        Result.Keystrokes.Style = style;
        RefreshPreview();
    }

    private void ReadKeystrokeControls()
    {
        Result.Keystrokes.Enabled = KeystrokesEnabledCheckBox.IsChecked == true;
        Result.Keystrokes.Scale = KeystrokeScaleSlider.Value;
        Result.Keystrokes.VisibleDurationMilliseconds = (int)KeystrokeDurationSlider.Value;
        Result.Keystrokes.MaximumStackEntries = (int)KeystrokeStackSlider.Value;
        Result.Keystrokes.Opacity = KeystrokeOpacitySlider.Value;
        Result.Keystrokes.FadeDurationMilliseconds = (int)KeystrokeFadeSlider.Value;
        Result.Keystrokes.MergeWindowMilliseconds = (int)KeystrokeMergeWindowSlider.Value;
        Result.Keystrokes.MergeCombinations = MergeCombinationsCheckBox.IsChecked == true;
        Result.Keystrokes.HideNormalTyping = Result.Keystrokes.DisplayMode != KeystrokeDisplayMode.AllKeys;
        Result.Keystrokes.HideRecorderHotkeys = HideRecorderHotkeysCheckBox.IsChecked == true;
    }

    private void TestKeystrokes_Click(object sender, RoutedEventArgs e)
    {
        KeystrokesEnabledCheckBox.IsChecked = true;
        RefreshPreview();
        KeystrokePreview.BeginAnimation(OpacityProperty, new DoubleAnimation
        {
            From = 1,
            To = 0,
            BeginTime = TimeSpan.FromMilliseconds(Result.Keystrokes.VisibleDurationMilliseconds),
            Duration = TimeSpan.FromMilliseconds(Result.Keystrokes.FadeDurationMilliseconds),
        });
    }

    private void ClickSetting_Changed(object sender, RoutedEventArgs e)
    {
        if (loading) return;
        HighlightClicks = ClicksEnabledCheckBox.IsChecked == true;
        Result.Clicks.Size = (int)Math.Round(ClickSizeSlider.Value);
        Result.Clicks.RingWidth = (int)Math.Round(ClickWidthSlider.Value);
        Result.Clicks.DurationMilliseconds = (int)Math.Round(ClickDurationSlider.Value);
        Result.Clicks.Opacity = ClickOpacitySlider.Value;
        RefreshPreview();
    }

    private void ClickPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string preset }) return;
        (Result.Clicks.LeftColor, Result.Clicks.RightColor) = preset switch
        {
            "Blue" => ("#3694FFFF", "#FF5364FF"),
            "Contrast" => ("#00FFFFFF", "#FFFF00FF"),
            _ => ("#7B61FFFF", "#FFB020FF"),
        };
        LeftClickColorBox.Text = Result.Clicks.LeftColor;
        RightClickColorBox.Text = Result.Clicks.RightColor;
        ClickColorError.Text = string.Empty;
        RefreshPreview();
    }

    private void ClickColor_LostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (loading) return;
        if (!TryNormalizeColor(LeftClickColorBox.Text, out var left) ||
            !TryNormalizeColor(RightClickColorBox.Text, out var right))
        {
            LeftClickColorBox.Text = Result.Clicks.LeftColor;
            RightClickColorBox.Text = Result.Clicks.RightColor;
            ClickColorError.Text = "Use #RRGGBB or #RRGGBBAA for both click colors.";
            return;
        }
        Result.Clicks.LeftColor = left;
        Result.Clicks.RightColor = right;
        ClickColorError.Text = string.Empty;
        RefreshPreview();
    }

    private void TestClick_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || !Enum.TryParse<MouseClickButton>(button.Tag?.ToString(), out var mouseButton)) return;
        ClicksEnabledCheckBox.IsChecked = true;
        RefreshClickPreview(mouseButton);
        var scale = new ScaleTransform(0.55, 0.55);
        ClickPreview.RenderTransform = scale;
        var duration = TimeSpan.FromMilliseconds(Result.Clicks.DurationMilliseconds);
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.55, 1.3, duration) { EasingFunction = easing });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.55, 1.3, duration) { EasingFunction = easing });
        ClickPreview.BeginAnimation(OpacityProperty, new DoubleAnimation(Result.Clicks.Opacity, 0, duration));
    }

    private void RefreshClickPreview(MouseClickButton button)
    {
        ClickPreview.BeginAnimation(OpacityProperty, null);
        ClickPreview.RenderTransform = Transform.Identity;
        ClickPreview.Visibility = HighlightClicks ? Visibility.Visible : Visibility.Collapsed;
        if (!HighlightClicks) return;
        var texture = new ClickRenderer(Result.Clicks).Textures[button];
        ClickPreview.Source = texture;
        ClickPreview.Width = texture.PixelWidth;
        ClickPreview.Height = texture.PixelHeight;
        ClickPreview.Opacity = Result.Clicks.Opacity;
        Canvas.SetLeft(ClickPreview, frameWidth * 0.55 - texture.PixelWidth / 2.0);
        Canvas.SetTop(ClickPreview, frameHeight * 0.55 - texture.PixelHeight / 2.0);
    }

    private void Overlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element)
        {
            return;
        }

        draggedElement = element;
        var position = e.GetPosition(PreviewCanvas);
        if (element == LabelPreview && HitLine(position) is { } selected)
        {
            LabelLinesEditor.SelectedItem = selected;
            if (e.ClickCount > 1)
            {
                draggedElement = null;
                BeginInlineEditing(selected);
                e.Handled = true;
                return;
            }
        }
        if ((element == LabelPreview || element == LabelResizeHandle) && renderedLabel is { } label)
        {
            dragOffset = new Point(position.X - label.Container.X, position.Y - label.Container.Y);
            resizeStart = position;
            resizeWidth = label.Container.Width;
            dragContainer = label.Container;
        }
        else dragOffset = new Point(position.X - Canvas.GetLeft(element), position.Y - Canvas.GetTop(element));
        element.CaptureMouse();
        e.Handled = true;
    }

    private void Overlay_MouseMove(object sender, MouseEventArgs e)
    {
        if (draggedElement is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var position = e.GetPosition(PreviewCanvas);
        if (draggedElement == LabelPreview && renderedLabel is { } label)
        {
            Result.Label.Anchor = OverlayAnchor.TopLeft;
            Result.Label.OffsetX = (int)Math.Round(Math.Clamp(position.X - dragOffset.X, 0, frameWidth - label.Container.Width));
            Result.Label.OffsetY = (int)Math.Round(Math.Clamp(position.Y - dragOffset.Y, 0, frameHeight - label.Container.Height));
            RefreshPreview();
            return;
        }
        if (draggedElement == LabelResizeHandle)
        {
            Result.Label.Anchor = OverlayAnchor.TopLeft;
            Result.Label.OffsetX = dragContainer.X;
            Result.Label.OffsetY = dragContainer.Y;
            var available = frameWidth - dragContainer.X;
            Result.Label.Width = (int)Math.Round(Math.Clamp(resizeWidth + position.X - resizeStart.X, Math.Min(80, available), available));
            loading = true; LabelWidthSlider.Value = Result.Label.Width; loading = false;
            RefreshPreview();
            return;
        }
        var left = Math.Clamp(position.X - dragOffset.X, 0, Math.Max(0, PreviewCanvas.ActualWidth - draggedElement.ActualWidth));
        var top = Math.Clamp(position.Y - dragOffset.Y, 0, Math.Max(0, PreviewCanvas.ActualHeight - draggedElement.ActualHeight));
        Canvas.SetLeft(draggedElement, left);
        Canvas.SetTop(draggedElement, top);
        if (draggedElement == KeystrokePreview)
        {
            Result.Keystrokes.Anchor = OverlayAnchor.TopLeft;
            Result.Keystrokes.OffsetX = (int)Math.Round(left);
            Result.Keystrokes.OffsetY = (int)Math.Round(top);
        }
    }

    private void Overlay_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (draggedElement is null)
        {
            return;
        }

        draggedElement.ReleaseMouseCapture();
        draggedElement = null;
        RefreshPreview();
        e.Handled = true;
    }

    private void Overlay_LostMouseCapture(object sender, MouseEventArgs e) => draggedElement = null;

    private LabelTextLine? HitLine(Point position)
    {
        if (renderedLabel is null) return null;
        var hit = renderedLabel.Lines.FirstOrDefault(line => position.X >= line.Bounds.X && position.X <= line.Bounds.Right &&
            position.Y >= line.Bounds.Y && position.Y <= line.Bounds.Bottom);
        return hit is null ? null : lines.FirstOrDefault(line => line.Id == hit.Id);
    }

    private void BeginInlineEditing(LabelTextLine line)
    {
        if (editingLine is not null) EndInlineEditing(commit: true);
        var layout = renderedLabel?.Lines.FirstOrDefault(candidate => candidate.Id == line.Id);
        if (layout is null) return;
        editingLine = line;
        originalInlineText = line.Text;
        inlineBounds = layout.Bounds;
        inlineUpdating = true;
        InlineTextEditor.Text = line.Text;
        InlineTextEditor.FontFamily = new FontFamily(line.FontFamily);
        InlineTextEditor.FontSize = line.Size;
        InlineTextEditor.FontWeight = line.IsBold ? FontWeights.Bold : FontWeights.Normal;
        InlineTextEditor.FontStyle = line.IsItalic ? FontStyles.Italic : FontStyles.Normal;
        InlineTextEditor.Foreground = LabelRenderer.Brush(line.Color);
        inlineUpdating = false;
        InlineTextEditor.Visibility = Visibility.Visible;
        UpdateInlineEditor();
        InlineTextEditor.Focus();
        InlineTextEditor.SelectAll();
    }

    private void UpdateInlineEditor()
    {
        if (editingLine is null) return;
        var layout = renderedLabel?.Lines.FirstOrDefault(candidate => candidate.Id == editingLine.Id);
        if (layout is not null) inlineBounds = layout.Bounds;
        InlineTextEditor.Width = inlineBounds.Width;
        InlineTextEditor.Height = Math.Max(34, inlineBounds.Height);
        Canvas.SetLeft(InlineTextEditor, inlineBounds.X);
        Canvas.SetTop(InlineTextEditor, Math.Max(0, inlineBounds.Y - (InlineTextEditor.Height - inlineBounds.Height) / 2));
    }

    private void InlineTextEditor_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (inlineUpdating || editingLine is null) return;
        editingLine.Text = InlineTextEditor.Text;
        QueuePreview();
    }

    private void InlineTextEditor_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            EndInlineEditing(commit: false);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
        {
            EndInlineEditing(commit: true);
            e.Handled = true;
        }
    }

    private void InlineTextEditor_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (editingLine is not null) EndInlineEditing(commit: true);
    }

    private void EndInlineEditing(bool commit)
    {
        if (editingLine is null) return;
        if (!commit) editingLine.Text = originalInlineText;
        editingLine = null;
        inlineUpdating = true;
        InlineTextEditor.Visibility = Visibility.Collapsed;
        InlineTextEditor.Text = string.Empty;
        inlineUpdating = false;
        RefreshPreview();
    }

    internal bool BeginInlineEditForChecks(int lineIndex)
    {
        if (lineIndex < 0 || lineIndex >= lines.Count) return false;
        LabelLinesEditor.SelectedItem = lines[lineIndex];
        BeginInlineEditing(lines[lineIndex]);
        return editingLine is not null;
    }

    internal void CommitInlineEditForChecks() => EndInlineEditing(commit: true);

    internal void CancelInlineEditForChecks() => EndInlineEditing(commit: false);

    internal void SetInlineTextForChecks(string text)
    {
        if (editingLine is null) throw new InvalidOperationException("Inline editing is not active.");
        InlineTextEditor.Text = text;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        EndInlineEditing(commit: true);
        ReadKeystrokeControls();
        Result.Label.Enabled = LabelEnabledCheckBox.IsChecked == true;
        Result.Label.Lines = lines.ToList();
        DialogResult = true;
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
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

        comboBox.SelectedIndex = 0;
    }

    private static string SelectedTag(ComboBox comboBox)
    {
        return (comboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? string.Empty;
    }

    private static OverlaySettings Clone(OverlaySettings source)
    {
        var json = JsonSerializer.Serialize(source);
        return JsonSerializer.Deserialize<OverlaySettings>(json)
            ?? throw new InvalidOperationException("Failed to copy overlay settings.");
    }
}
