using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using OSCQueryExplorer.Core.History;
using OSCQueryExplorer.Core.Models;
using OSCQueryExplorer.ViewModels;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using CoreAppTheme = OSCQueryExplorer.Core.Settings.AppTheme;
using WpfApplicationTheme = Wpf.Ui.Appearance.ApplicationTheme;

namespace OSCQueryExplorer;

public partial class MainWindow : FluentWindow
{
    private readonly MainViewModel _viewModel = new();
    private Point _pinnedDragStart;
    private PinnedItemViewModel? _pinnedDragItem;
    private PinnedItemViewModel? _pinnedDropTarget;
    private ListBoxItem? _pinnedDropContainer;
    private bool _pinnedDropAfter;
    private double _treeHorizontalOffset;
    private double _treeVerticalOffset;
    private int _treeRestoreVersion;
    private bool _isClosing;
    private bool _canClose;
    public MainWindow()
    {
        InitializeComponent(); DataContext = _viewModel;
        Loaded += async (_, _) => await ExecuteUiActionAsync(async () =>
        {
            await _viewModel.InitializeAsync();
            RestorePaneSizes();
            ApplyConfiguredTheme();
            await CheckForUpdatesAndNotifyAsync(false, false);
        });
        _viewModel.VisibleLog.CollectionChanged += GlobalLog_CollectionChanged;
        _viewModel.ParameterLog.CollectionChanged += ParameterLog_CollectionChanged;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        _viewModel.TreeUpdating += Tree_Updating;
        _viewModel.TreeUpdated += Tree_Updated;
    }
    private void Tree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e) => _viewModel.SelectedNode = e.NewValue as OscNode;
    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteUiActionAsync(_viewModel.ConnectAsync);
        if (_viewModel.IsConnected) ConnectionOverlay.Visibility = Visibility.Collapsed;
    }
    private async void ConnectionService_Click(object sender, MouseButtonEventArgs e)
    {
        if (FindDataContext<DiscoveredService>(e.OriginalSource as DependencyObject) is not { } service) return;
        _viewModel.SelectedService = service;
        _viewModel.ManualUrl = service.HttpEndpoint.ToString();
        await ExecuteUiActionAsync(_viewModel.ConnectAsync);
        if (_viewModel.IsConnected) ConnectionOverlay.Visibility = Visibility.Collapsed;
    }
    private void ShowConnection_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.SelectedService = null;
        ConnectionOverlay.Visibility = Visibility.Visible;
    }
    private async void Argument_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (FocusMovedToExplorer(e) || sender is not FrameworkElement { DataContext: ArgumentEditorViewModel editor } || !_viewModel.ArgumentEditors.Contains(editor)) return;
        await ExecuteUiActionAsync(_viewModel.SendIfImmediateAsync);
    }
    private async void Argument_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { await ExecuteUiActionAsync(_viewModel.SendIfImmediateAsync); e.Handled = true; } }
    private async void ArgumentToggle_Click(object sender, RoutedEventArgs e) => await ExecuteUiActionAsync(_viewModel.SendIfImmediateAsync);
    private async void ArgumentSlider_MouseUp(object sender, MouseButtonEventArgs e) => await ExecuteUiActionAsync(_viewModel.SendIfImmediateAsync);
    private async void PinnedSend_Click(object sender, RoutedEventArgs e)
    {
        if (FindDataContext<PinnedItemViewModel>(sender as DependencyObject) is { } item)
            await ExecuteUiActionAsync(() => _viewModel.SendPinnedAsync(item));
    }
    private async void PinnedToggle_Click(object sender, RoutedEventArgs e) => await SendPinnedFromElementAsync(sender);
    private async void PinnedSlider_MouseUp(object sender, MouseButtonEventArgs e) => await SendPinnedFromElementAsync(sender);
    private async void PinnedEditor_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (FocusMovedToExplorer(e)) return;
        await SendPinnedFromElementAsync(sender);
    }
    private async void PinnedEditor_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        await SendPinnedFromElementAsync(sender); e.Handled = true;
    }
    private async Task SendPinnedFromElementAsync(object sender)
    {
        if (FindDataContext<PinnedItemViewModel>(sender as DependencyObject) is { } item)
            await ExecuteUiActionAsync(() => _viewModel.SendPinnedAsync(item));
    }
    private bool FocusMovedToExplorer(KeyboardFocusChangedEventArgs e) =>
        e.NewFocus is DependencyObject element && ReferenceEquals(FindVisualAncestor<TreeView>(element), ExplorerTree);
    private async void PinnedUnpin_Click(object sender, RoutedEventArgs e)
    {
        if (FindDataContext<PinnedItemViewModel>(sender as DependencyObject) is { } item)
            await ExecuteUiActionAsync(() => _viewModel.TogglePinAsync(item.Node));
    }

    private void PinnedList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _pinnedDragStart = e.GetPosition(PinnedList);
        var source = e.OriginalSource as DependencyObject;
        _pinnedDragItem = IsPinnedDragHandle(source) ? FindDataContext<PinnedItemViewModel>(source) : null;
        if (_pinnedDragItem is not null) e.Handled = true;
    }
    private void PinnedList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _pinnedDragItem is null) return;
        var point = e.GetPosition(PinnedList);
        if (Math.Abs(point.X - _pinnedDragStart.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(point.Y - _pinnedDragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        try { DragDrop.DoDragDrop(PinnedList, _pinnedDragItem, DragDropEffects.Move); }
        finally { _pinnedDragItem = null; ClearPinnedDropIndicator(); }
    }
    private void PinnedList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) => _pinnedDragItem = null;
    private void PinnedList_MouseLeave(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) ClearPinnedDropIndicator();
    }
    private void PinnedList_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(PinnedItemViewModel)) is not PinnedItemViewModel source)
        {
            e.Effects = DragDropEffects.None;
            ClearPinnedDropIndicator();
            return;
        }

        var container = FindVisualAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (container is null && PinnedNodesLastContainer() is { } lastContainer)
            container = lastContainer;
        if (container?.DataContext is not PinnedItemViewModel target || ReferenceEquals(source, target))
        {
            e.Effects = DragDropEffects.None;
            ClearPinnedDropIndicator();
            return;
        }

        var insertAfter = e.GetPosition(container).Y >= container.ActualHeight / 2;
        SetPinnedDropIndicator(container, target, insertAfter);
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }
    private async void PinnedList_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(PinnedItemViewModel)) is not PinnedItemViewModel source || _pinnedDropTarget is not { } target) return;
        var insertAfter = _pinnedDropAfter;
        ClearPinnedDropIndicator();
        await ExecuteUiActionAsync(() => _viewModel.MovePinnedAsync(source, target, insertAfter));
        e.Handled = true;
    }
    private void SetPinnedDropIndicator(ListBoxItem container, PinnedItemViewModel target, bool insertAfter)
    {
        if (!ReferenceEquals(_pinnedDropContainer, container) && _pinnedDropContainer is not null) _pinnedDropContainer.Tag = null;
        _pinnedDropContainer = container;
        _pinnedDropTarget = target;
        _pinnedDropAfter = insertAfter;
        container.Tag = insertAfter ? "DropAfter" : "DropBefore";
    }
    private void ClearPinnedDropIndicator()
    {
        if (_pinnedDropContainer is not null) _pinnedDropContainer.Tag = null;
        _pinnedDropContainer = null;
        _pinnedDropTarget = null;
        _pinnedDropAfter = false;
    }
    private ListBoxItem? PinnedNodesLastContainer() => _viewModel.PinnedNodes.Count == 0
        ? null
        : PinnedList.ItemContainerGenerator.ContainerFromItem(_viewModel.PinnedNodes[^1]) as ListBoxItem;
    private static bool IsPinnedDragHandle(DependencyObject? element)
    {
        for (var current = element; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is FrameworkElement { Tag: "PinnedDragHandle" }) return true;
            if (current is ListBoxItem) return false;
        }
        return false;
    }
    private static T? FindVisualAncestor<T>(DependencyObject? element) where T : DependencyObject
    {
        for (var current = element; current is not null; current = VisualTreeHelper.GetParent(current))
            if (current is T match) return match;
        return null;
    }
    private static T? FindDataContext<T>(DependencyObject? element) where T : class
    {
        for (var current = element; current is not null; current = VisualTreeHelper.GetParent(current))
            if (current is FrameworkElement { DataContext: T value }) return value;
        return null;
    }
    private async void AddCustomNode_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new CustomNodeWindow { Owner = this };
        if (dialog.ShowDialog() == true)
            await ExecuteUiActionAsync(() => _viewModel.AddCustomNodeAsync(dialog.NodeAddress, dialog.TypeTag));
    }
    private async void SliderRangeSettings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SliderRangeSettingsWindow(_viewModel.GetSliderRangeTreeRoot(), _viewModel.GetSliderRangeRules()) { Owner = this };
        if (dialog.ShowDialog() == true)
            await ExecuteUiActionAsync(() => _viewModel.SetSliderRangeRulesAsync(dialog.Rules));
    }
    private async void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(_viewModel.GetPreferences(), () => CheckForUpdatesAndNotifyAsync(true, true)) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        await ExecuteUiActionAsync(() => _viewModel.ApplyPreferencesAsync(dialog.Preferences));
        ApplyConfiguredTheme();
    }

    private async Task CheckForUpdatesAndNotifyAsync(bool force, bool showUpToDate)
    {
        try
        {
            var release = await _viewModel.CheckForUpdatesAsync(force);
            if (release is null)
            {
                if (showUpToDate)
                    System.Windows.MessageBox.Show(this, "利用可能な新しいバージョンはありません。", "更新確認",
                        System.Windows.MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var notes = string.IsNullOrWhiteSpace(release.Notes)
                ? string.Empty
                : "\n\n" + (release.Notes.Length > 1200 ? release.Notes[..1200] + "…" : release.Notes);
            var result = System.Windows.MessageBox.Show(this,
                $"OSCQuery Explorer {release.Tag} が利用可能です。\n\nリリースページを開きますか？{notes}",
                "更新があります", System.Windows.MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (result == System.Windows.MessageBoxResult.Yes)
                Process.Start(new ProcessStartInfo(release.PageUrl) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            if (force)
                System.Windows.MessageBox.Show(this, $"更新を確認できませんでした。\n\n{exception.Message}", "更新確認",
                    System.Windows.MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
    private static OscNode? NodeFromMenu(object sender) => (sender as System.Windows.Controls.MenuItem)?.DataContext as OscNode;
    private async void NodePin_Click(object sender, RoutedEventArgs e) { if (NodeFromMenu(sender) is { } node) await ExecuteUiActionAsync(() => _viewModel.TogglePinAsync(node)); }
    private async void NodePublish_Click(object sender, RoutedEventArgs e) { if (NodeFromMenu(sender) is { } node) await ExecuteUiActionAsync(() => _viewModel.TogglePublishedAsync(node)); }
    private void NodeCopy_Click(object sender, RoutedEventArgs e) { if (NodeFromMenu(sender) is { } node) Clipboard.SetText(node.FullPath); }

    private void GlobalLog_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => ScrollToLatestIfFollowing(GlobalLogList);
    private void ParameterLog_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => ScrollToLatestIfFollowing(ParameterLogList);
    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.Follow) || !_viewModel.Follow) return;
        ScrollToLatestIfFollowing(GlobalLogList);
        ScrollToLatestIfFollowing(ParameterLogList);
    }
    private void ScrollToLatestIfFollowing(ListBox list)
    {
        if (!_viewModel.Follow || list.Items.Count == 0) return;
        Dispatcher.BeginInvoke(() =>
        {
            if (!_viewModel.Follow || list.Items.Count == 0) return;
            list.ScrollIntoView(list.Items[list.Items.Count - 1]);
        }, System.Windows.Threading.DispatcherPriority.Background);
    }
    private void Tree_Updating(object? sender, EventArgs e)
    {
        if (FindVisualDescendant<ScrollViewer>(ExplorerTree) is not { } scrollViewer) return;
        _treeHorizontalOffset = scrollViewer.HorizontalOffset;
        _treeVerticalOffset = scrollViewer.VerticalOffset;
    }
    private void Tree_Updated(object? sender, EventArgs e)
    {
        var version = ++_treeRestoreVersion;
        Dispatcher.BeginInvoke(() =>
        {
            if (version != _treeRestoreVersion || FindVisualDescendant<ScrollViewer>(ExplorerTree) is not { } scrollViewer) return;
            scrollViewer.ScrollToHorizontalOffset(_treeHorizontalOffset);
            scrollViewer.ScrollToVerticalOffset(_treeVerticalOffset);
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }
    private static T? FindVisualDescendant<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) return match;
            if (FindVisualDescendant<T>(child) is { } descendant) return descendant;
        }
        return null;
    }
    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Title = "表示中のログをエクスポート", Filter = "Text file (*.txt)|*.txt|CSV file (*.csv)|*.csv", AddExtension = true, DefaultExt = ".txt" };
        if (dialog.ShowDialog(this) != true) return;
        var entries = _viewModel.GetVisibleLogSnapshot();
        await ExecuteUiActionAsync(() => Path.GetExtension(dialog.FileName).Equals(".csv", StringComparison.OrdinalIgnoreCase)
            ? HistoryExporter.ExportCsvAsync(dialog.FileName, entries, _viewModel.TypeDisplay)
            : HistoryExporter.ExportTextAsync(dialog.FileName, entries, _viewModel.TypeDisplay));
    }
    private async Task ExecuteUiActionAsync(Func<Task> action)
    {
        try { await action(); }
        catch (OperationCanceledException) when (_isClosing) { }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(this, $"操作を完了できませんでした。\n\n{exception.Message}", "OSCQuery Explorer",
                System.Windows.MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
    private void ApplyConfiguredTheme()
    {
        if (_viewModel.ConfiguredTheme == CoreAppTheme.System)
        {
            ApplicationThemeManager.ApplySystemTheme();
            SystemThemeWatcher.Watch(this);
            return;
        }

        SystemThemeWatcher.UnWatch(this);
        ApplicationThemeManager.Apply(
            _viewModel.ConfiguredTheme == CoreAppTheme.Dark ? WpfApplicationTheme.Dark : WpfApplicationTheme.Light
        );
    }
    private void RestorePaneSizes()
    {
        var panes = _viewModel.PaneSettings;
        ExplorerColumn.Width = PixelLength(panes.ExplorerWidth, 190);
        InspectorColumn.Width = PixelLength(panes.InspectorWidth, 300);
        // Keep one pane flexible so the pinned pane remains aligned with the
        // window's right inset instead of leaving unused space on the right.
        ParameterLogColumn.Width = new GridLength(1, GridUnitType.Star);
        PinnedColumn.Width = PixelLength(panes.PinnedWidth, 220);
        LogRow.Height = PixelLength(panes.LogHeight, 160);
    }
    private void CapturePaneSizes()
    {
        var panes = _viewModel.PaneSettings;
        panes.ExplorerWidth = ValidActualSize(ExplorerColumn.ActualWidth, 190);
        panes.InspectorWidth = ValidActualSize(InspectorColumn.ActualWidth, 300);
        panes.PinnedWidth = ValidActualSize(PinnedColumn.ActualWidth, 220);
        panes.LogHeight = ValidActualSize(LogRow.ActualHeight, 160);
    }
    private static GridLength PixelLength(double value, double minimum) =>
        new(double.IsFinite(value) ? Math.Max(value, minimum) : minimum, GridUnitType.Pixel);
    private static double ValidActualSize(double value, double minimum) =>
        double.IsFinite(value) && value >= minimum ? value : minimum;
    protected override void OnClosing(CancelEventArgs e)
    {
        if (_canClose)
        {
            base.OnClosing(e);
            return;
        }

        e.Cancel = true;
        if (_isClosing)
        {
            base.OnClosing(e);
            return;
        }

        _isClosing = true;
        CapturePaneSizes();
        SystemThemeWatcher.UnWatch(this);
        _viewModel.VisibleLog.CollectionChanged -= GlobalLog_CollectionChanged;
        _viewModel.ParameterLog.CollectionChanged -= ParameterLog_CollectionChanged;
        _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        _viewModel.TreeUpdating -= Tree_Updating;
        _viewModel.TreeUpdated -= Tree_Updated;
        _ = CompleteShutdownAsync();
        base.OnClosing(e);
    }

    private async Task CompleteShutdownAsync()
    {
        try
        {
            await _viewModel.DisposeAsync();
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"終了処理に失敗しました: {exception}");
        }

        if (Dispatcher.HasShutdownStarted) return;
        _canClose = true;
        Application.Current.Shutdown();
    }
}
