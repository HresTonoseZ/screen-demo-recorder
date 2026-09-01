using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using ScreenDemoRecorder.Core.Models;

namespace ScreenDemoRecorder;

internal static class ApplicationThemeManager
{
    private static ApplicationTheme preference = ApplicationTheme.Dark;
    private static bool initialized;
    private static bool lightPalette;

    public static void Initialize()
    {
        if (initialized) return;
        initialized = true;
        SystemEvents.UserPreferenceChanged += UserPreferenceChanged;
    }

    public static void Shutdown()
    {
        if (!initialized) return;
        SystemEvents.UserPreferenceChanged -= UserPreferenceChanged;
        initialized = false;
    }

    public static void Apply(ApplicationTheme requestedTheme)
    {
        preference = requestedTheme;
        var application = Application.Current;
        if (application is null) return;
        var resolved = requestedTheme == ApplicationTheme.System
            ? (WindowsUsesLightTheme() ? ApplicationTheme.Light : ApplicationTheme.Dark)
            : requestedTheme;
        lightPalette = resolved == ApplicationTheme.Light;
        var palette = resolved == ApplicationTheme.Light ? LightPalette : DarkPalette;
        foreach (var (key, color) in palette)
            application.Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        foreach (Window window in application.Windows) ApplyToWindow(window);
    }

    internal static ApplicationTheme Preference => preference;

    internal static void ApplyToWindow(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == nint.Zero) return;
        var dark = lightPalette ? 0 : 1;
        _ = DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
    }

    private static bool WindowsUsesLightTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int value && value != 0;
    }

    private static void UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (preference != ApplicationTheme.System || Application.Current is not { } application) return;
        _ = application.Dispatcher.InvokeAsync(() => Apply(ApplicationTheme.System));
    }

    private static readonly IReadOnlyDictionary<string, string> DarkPalette = new Dictionary<string, string>
    {
        ["WindowBrush"] = "#17191E",
        ["SurfaceBrush"] = "#24272E",
        ["RaisedBrush"] = "#30343D",
        ["BorderBrush"] = "#404550",
        ["TextBrush"] = "#F5F7FA",
        ["MutedBrush"] = "#AEB5C0",
        ["AccentBrush"] = "#9B82FF",
        ["AccentDarkBrush"] = "#6A48E8",
        ["AccentTextBrush"] = "#FFFFFF",
    };

    private static readonly IReadOnlyDictionary<string, string> LightPalette = new Dictionary<string, string>
    {
        ["WindowBrush"] = "#F4F6FA",
        ["SurfaceBrush"] = "#FFFFFF",
        ["RaisedBrush"] = "#E9EDF3",
        ["BorderBrush"] = "#C7CED9",
        ["TextBrush"] = "#17191E",
        ["MutedBrush"] = "#626A76",
        ["AccentBrush"] = "#7659EF",
        ["AccentDarkBrush"] = "#6846E2",
        ["AccentTextBrush"] = "#FFFFFF",
    };

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int valueSize);
}
