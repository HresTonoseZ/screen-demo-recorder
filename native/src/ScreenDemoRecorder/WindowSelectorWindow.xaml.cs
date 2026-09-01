using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;

namespace ScreenDemoRecorder;

public partial class WindowSelectorWindow : Window
{
    private readonly ObservableCollection<DesktopWindowInfo> windows = [];
    private readonly ICollectionView view;
    private readonly bool live;
    public DesktopWindowInfo? Result { get; private set; }
    internal int VisibleWindowCount => view.Cast<object>().Count();

    public WindowSelectorWindow(DesktopWindowInfo? previous = null)
        : this(NativeDesktop.Windows(), previous, true) { }

    internal WindowSelectorWindow(IEnumerable<DesktopWindowInfo> items, DesktopWindowInfo? previous, bool useLiveWindows)
    {
        InitializeComponent();
        live = useLiveWindows;
        foreach (var item in items) windows.Add(item);
        view = CollectionViewSource.GetDefaultView(windows);
        view.Filter = MatchesSearch;
        WindowList.ItemsSource = view;
        WindowList.SelectedItem = previous is null ? null : windows.FirstOrDefault(item => item.Handle == previous.Handle);
        SourceInitialized += (_, _) => NativeDesktop.Exclude(this);
        UpdateEmptyState();
    }

    private bool MatchesSearch(object item)
    {
        if (item is not DesktopWindowInfo window) return false;
        var search = SearchBox.Text.Trim();
        return search.Length == 0 || window.Title.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
            window.ProcessName.Contains(search, StringComparison.CurrentCultureIgnoreCase);
    }

    private void Search_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (view is null) return;
        view.Refresh();
        UpdateEmptyState();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        var selectedHandle = (WindowList.SelectedItem as DesktopWindowInfo)?.Handle ?? 0;
        try
        {
            windows.Clear();
            foreach (var item in NativeDesktop.Windows()) windows.Add(item);
            WindowList.SelectedItem = windows.FirstOrDefault(item => item.Handle == selectedHandle);
            view.Refresh();
            UpdateEmptyState();
        }
        catch (Win32Exception error) { SelectionHint.Text = $"Cannot refresh: {error.Message}"; }
    }

    private void WindowList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        var selected = WindowList.SelectedItem as DesktopWindowInfo;
        UseButton.IsEnabled = selected is { IsMinimized: false };
        SelectionHint.Text = selected is null ? "Select a window" : selected.IsMinimized
            ? "Restore this window, then click Refresh."
            : $"{selected.Bounds.Width} × {selected.Bounds.Height} · {selected.ProcessName}";
    }

    private void WindowList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (UseButton.IsEnabled) Accept();
    }

    private void Use_Click(object sender, RoutedEventArgs e) => Accept();

    internal bool TryAcceptForChecks()
    {
        Accept();
        return Result is not null;
    }

    private void Accept()
    {
        if (WindowList.SelectedItem is not DesktopWindowInfo selected || selected.IsMinimized) return;
        if (live)
        {
            if (!NativeDesktop.TryGetWindow(selected.Handle, out var current) || current.ProcessId != selected.ProcessId || current.ClassName != selected.ClassName)
            {
                SelectionHint.Text = "That window was closed. Click Refresh and choose another.";
                UseButton.IsEnabled = false;
                return;
            }
            if (current.IsMinimized)
            {
                SelectionHint.Text = "Restore this window, then click Refresh.";
                UseButton.IsEnabled = false;
                return;
            }
            selected = current;
        }
        Result = selected;
        if (IsVisible) DialogResult = true;
    }

    private void UpdateEmptyState()
    {
        EmptyText.Visibility = VisibleWindowCount == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
