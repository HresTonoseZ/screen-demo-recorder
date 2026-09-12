using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ScreenDemoRecorder.Core.Models;

namespace ScreenDemoRecorder;

internal sealed class CountdownOverlayWindow : IDisposable
{
    private readonly Window window;
    private readonly TextBlock number;
    private readonly PixelRect bounds;

    public CountdownOverlayWindow(PixelRect captureBounds)
    {
        var size = Math.Max(1, Math.Min(180, Math.Min(captureBounds.Width, captureBounds.Height)));
        bounds = new PixelRect(
            captureBounds.X + (captureBounds.Width - size) / 2,
            captureBounds.Y + (captureBounds.Height - size) / 2,
            size,
            size);
        number = new TextBlock
        {
            Foreground = Brushes.White,
            FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI"),
            FontSize = Math.Max(12, size * 0.5),
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        window = new Window
        {
            Title = "Recording countdown",
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
            ShowActivated = false,
            Focusable = false,
            Topmost = true,
            Width = 1,
            Height = 1,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -32000,
            Top = -32000,
            Content = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(225, 20, 24, 35)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(123, 97, 255)),
                BorderThickness = new Thickness(Math.Max(2, size / 60.0)),
                CornerRadius = new CornerRadius(size / 2.0),
                Child = number,
            },
        };
        window.Show();
        NativeDesktop.Place(window, bounds, true);
    }

    public bool IsVisible => window.IsVisible;
    public bool IsExcluded => NativeDesktop.IsExcluded(window);
    public bool IsPassive => NativeDesktop.IsPassiveOverlay(window);
    public bool IsCenteredIn(PixelRect captureBounds) =>
        bounds.X + bounds.Width / 2 == captureBounds.X + captureBounds.Width / 2 &&
        bounds.Y + bounds.Height / 2 == captureBounds.Y + captureBounds.Height / 2;

    public void ShowNumber(int value) => number.Text = value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public void Dispose() => window.Close();
}
