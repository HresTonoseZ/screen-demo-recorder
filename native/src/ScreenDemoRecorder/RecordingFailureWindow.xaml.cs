using System.Diagnostics;
using System.IO;
using System.Windows;
using ScreenDemoRecorder.Capture;

namespace ScreenDemoRecorder;

public partial class RecordingFailureWindow : Window
{
    private readonly string? recoveryPath;
    public bool UseSafeSettings { get; private set; }

    internal RecordingFailureWindow(RecordingFailureException failure)
    {
        InitializeComponent();
        recoveryPath = failure.RecoveryPath;
        MessageText.Text = failure.Summary;
        RecoveryPathText.Text = recoveryPath;
        RecoveryPanel.Visibility = recoveryPath is null ? Visibility.Collapsed : Visibility.Visible;
        SafeSettingsPanel.Visibility = SafeSettingsButton.Visibility = failure.CanUseSafeSettings ? Visibility.Visible : Visibility.Collapsed;
        SourceInitialized += (_, _) => NativeDesktop.Exclude(this);
    }

    private void SafeSettings_Click(object sender, RoutedEventArgs e)
    {
        UseSafeSettings = true;
        DialogResult = true;
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var directory = recoveryPath is null ? null : Path.GetDirectoryName(recoveryPath);
        if (directory is not null && Directory.Exists(directory))
            Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
    }
}
