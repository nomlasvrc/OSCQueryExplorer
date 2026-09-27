using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Threading;
using OSCQueryExplorer.Core.History;
using OSCQueryExplorer.Core.Models;
using OSCQueryExplorer.Core.Settings;
using OSCQueryExplorer.Core.Tree;
using OSCQueryExplorer.Protocol.Discovery;
using OSCQueryExplorer.Protocol.Osc;
using OSCQueryExplorer.Protocol.OscQuery;
using OSCQueryExplorer.Protocol.Updates;

namespace OSCQueryExplorer.ViewModels;

public sealed record MetadataEntry(string Label, string Value);
public sealed record TypeDisplayOption(TypeDisplayFormat Value, string Label);

public sealed class MainViewModel : ObservableObject, IAsyncDisposable
{
    private const string RepositoryUrl = "https://github.com/nomlasvrc/OSCQueryExplorer";
    public event EventHandler? TreeUpdating;
    public event EventHandler? TreeUpdated;
    private readonly SettingsStore _settingsStore = new();
    private readonly HistoryStore _history = new();
    private readonly NodeTree _tree = new();
    private readonly OscQueryClient _query = new();
    private readonly UdpOscTransport _udp = new();
    private readonly MdnsOscQueryDiscovery _discovery = new();
    private readonly GitHubUpdateChecker _updateChecker = new();
    private readonly SemaphoreSlim _connectionGate = new(1, 1);
    private readonly DispatcherTimer _logTimer;
    private readonly DispatcherTimer _valueTimer;
    private readonly DispatcherTimer _treeRefreshTimer;
    private readonly ConcurrentDictionary<string, ObservedValue> _pendingValues = new(StringComparer.Ordinal);
    private LocalOscQueryServer? _localServer;
    private CancellationTokenSource? _connectionCts;
    private Task? _receiverTask;
    private Task? _webSocketTask;
    private AppSettings _settings = new();
    private OscQueryHostInfo? _hostInfo;
    private Uri? _endpoint;
    private OscNode? _selectedNode;
    private DiscoveredService? _selectedService;
    private string _manualUrl = "http://127.0.0.1:9001/";
    private string _treeSearch = string.Empty;
    private string _logFilter = string.Empty;
    private string _status = "未接続";
    private bool _isConnected;
    private bool _isPaused;
    private bool _follow = true;
    private bool _sendImmediately = true;
    private string? _currentServiceKey;
    private volatile bool _logDirty;
    private bool _isConnecting;
    private string _connectionError = string.Empty;
    private bool _treeRefreshInProgress;
    private string? _renderedLogFilter;
    private TypeDisplayFormat? _renderedTypeDisplay;
    private long _lastRenderedHistoryId;
    private bool _localSnapshotDirty;
    private bool _isVrChatService;
    private long _nextLocalSnapshotRefresh;

    public ObservableCollection<OscNode> RootNodes { get; } = [];
    public ObservableCollection<DiscoveredService> DiscoveredServices { get; } = [];
    public ObservableCollection<PinnedItemViewModel> PinnedNodes { get; } = [];
    public BatchObservableCollection<LogEntryViewModel> VisibleLog { get; } = [];
    public BatchObservableCollection<LogEntryViewModel> ParameterLog { get; } = [];
    public ObservableCollection<ArgumentEditorViewModel> ArgumentEditors { get; } = [];
    public ObservableCollection<MetadataEntry> SelectedMetadata { get; } = [];
    public IReadOnlyList<TypeDisplayOption> TypeDisplayOptions { get; } =
    [
        new(TypeDisplayFormat.TypeTag, "タイプタグ"),
        new(TypeDisplayFormat.TypeName, "型名")
    ];
    public string ManualUrl { get => _manualUrl; set { if (Set(ref _manualUrl, value)) ReconnectCommand?.RaiseCanExecuteChanged(); } }
    public string TreeSearch { get => _treeSearch; set { if (Set(ref _treeSearch, value)) RefreshTree(); } }
    public string LogFilter { get => _logFilter; set { if (Set(ref _logFilter, value)) RefreshLog(); } }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public double LogFontSize => _settings.LogFontSize;
    public string? AppVersion => typeof(MainViewModel).Assembly.GetName().Version?.ToString(3);
    public bool IsConnected { get => _isConnected; private set { if (Set(ref _isConnected, value)) Raise(nameof(IsDisconnected)); } }
    public bool IsDisconnected => !IsConnected;
    public bool IsPaused { get => _isPaused; set { if (Set(ref _isPaused, value) && !value) RefreshLog(); } }
    public bool Follow { get => _follow; set => Set(ref _follow, value); }
    public bool RecordOsc { get => _history.RecordOsc; set { _history.RecordOsc = value; Raise(); } }
    public bool IsConnecting { get => _isConnecting; private set => Set(ref _isConnecting, value); }
    public string ConnectionError
    {
        get => _connectionError;
        private set
        {
            if (!Set(ref _connectionError, value)) return;
            Raise(nameof(HasConnectionError));
        }
    }
    public bool HasConnectionError => !string.IsNullOrWhiteSpace(ConnectionError);
    public TypeDisplayFormat TypeDisplay
    {
        get => _settings.TypeDisplay;
        set
        {
            if (_settings.TypeDisplay == value) return;
            _settings.TypeDisplay = value;
            Raise();
            BuildMetadata();
            foreach (var editor in ArgumentEditors) editor.DisplayFormat = value;
            foreach (var item in PinnedNodes) item.SetTypeDisplay(value);
            foreach (var item in VisibleLog) item.SetTypeDisplay(value);
            if (!IsPaused) RefreshLog();
            _ = SaveSettingsAsync();
        }
    }
    public string SelectedValueText => SelectedNode?.Observed is { Values.Count: > 0 } observed
        ? string.Join("  ·  ", observed.Values.Select(value => value.ToDisplayString()))
        : "—";
    public string SelectedValueStatus => SelectedNode?.Observed is { } observed
        ? $"{observed.UpdatedAt:HH:mm:ss.fff}  ·  {observed.Origin switch { ValueOrigin.UdpReceived => "OSC received", ValueOrigin.OscQuery => "OSCQuery", _ => "Unknown" }}"
        : "値はまだ受信されていません";
    public bool IsSelectedNodeWritable => SelectedNode is { } node && (node.Access is null || node.Access.Value.HasFlag(OscAccess.Write));
    public bool IsSelectedNodeReadOnly => SelectedNode is not null && !IsSelectedNodeWritable;
    public OscNode? SelectedNode
    {
        get => _selectedNode;
        set
        {
            if (ReferenceEquals(_selectedNode, value)) return;
            var previous = _selectedNode;
            if (previous is not null)
            {
                previous.PropertyChanged -= SelectedNode_PropertyChanged;
                previous.IsSelected = false;
            }
            if (!Set(ref _selectedNode, value)) return;
            var current = _selectedNode;
            if (current is not null)
            {
                current.PropertyChanged += SelectedNode_PropertyChanged;
                current.IsSelected = true;
            }
            RefreshParameterLog(); BuildEditors(); PinCommand.RaiseCanExecuteChanged();
            BuildMetadata();
            Raise(nameof(SelectedValueText)); Raise(nameof(SelectedValueStatus));
            Raise(nameof(IsSelectedNodeWritable)); Raise(nameof(IsSelectedNodeReadOnly));
        }
    }
    public bool SendImmediately { get => _sendImmediately; set => Set(ref _sendImmediately, value); }
    public DiscoveredService? SelectedService { get => _selectedService; set { if (Set(ref _selectedService, value) && value is not null) ManualUrl = value.HttpEndpoint.ToString(); } }

    public AsyncRelayCommand ConnectCommand { get; }
    public AsyncRelayCommand ReconnectCommand { get; }
    public AsyncRelayCommand DisconnectCommand { get; }
    public AsyncRelayCommand RefreshTreeCommand { get; }
    public RelayCommand ClearLogCommand { get; }
    public AsyncRelayCommand PinCommand { get; }
    public RelayCommand RefreshDiscoveryCommand { get; }
    public AsyncRelayCommand SendCommand { get; }
    public IReadOnlyList<HistoryEntry> GetVisibleLogSnapshot() => VisibleLog.Select(item => item.Entry).ToArray();
    public IReadOnlyList<SliderRangeRule> GetSliderRangeRules() => _settings.SliderRanges
        .Select(rule => new SliderRangeRule { PathPrefix = rule.PathPrefix, Minimum = rule.Minimum, Maximum = rule.Maximum, TypeTags = rule.TypeTags ?? string.Empty })
        .ToArray();
    public OscNode GetSliderRangeTreeRoot() => _tree.Root;
    public PaneSettings PaneSettings => _settings.Panes;
    public AppTheme ConfiguredTheme => _settings.Theme;
    public AppPreferences GetPreferences() => new(
        _settings.Theme,
        _settings.LogFontSize,
        _settings.PollingIntervalSeconds,
        _settings.PublishToLan,
        _settings.CheckUpdatesAutomatically);
    public Task SaveAsync() => SaveSettingsAsync();

    public async Task ApplyPreferencesAsync(AppPreferences preferences)
    {
        _settings.Theme = preferences.Theme;
        _settings.LogFontSize = Math.Clamp(preferences.LogFontSize, 9, 30);
        _settings.PollingIntervalSeconds = Math.Clamp(preferences.PollingIntervalSeconds, 1, 300);
        _settings.PublishToLan = preferences.PublishToLan;
        _settings.CheckUpdatesAutomatically = preferences.CheckUpdatesAutomatically;
        _treeRefreshTimer.Interval = TimeSpan.FromSeconds(_settings.PollingIntervalSeconds);
        Raise(nameof(LogFontSize));
        Raise(nameof(ConfiguredTheme));
        await SaveSettingsAsync();
    }

    public async Task<ReleaseInfo?> CheckForUpdatesAsync(bool force)
    {
        if (!force)
        {
            if (!_settings.CheckUpdatesAutomatically) return null;
            if (_settings.LastUpdateCheck is { } lastCheck && DateTimeOffset.UtcNow - lastCheck < TimeSpan.FromHours(24)) return null;
        }

        _settings.LastUpdateCheck = DateTimeOffset.UtcNow;
        await SaveSettingsAsync();
        var currentVersion = typeof(MainViewModel).Assembly.GetName().Version ?? new Version(0, 1, 0);
        return await _updateChecker.CheckAsync(RepositoryUrl, currentVersion);
    }

    private void SelectedNode_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OscNode.Observed))
        {
            Raise(nameof(SelectedValueText)); Raise(nameof(SelectedValueStatus));
        }
        else if (e.PropertyName is nameof(OscNode.TypeTag) or nameof(OscNode.Access))
        {
            BuildMetadata();
            BuildEditors();
            Raise(nameof(IsSelectedNodeWritable)); Raise(nameof(IsSelectedNodeReadOnly));
            CommandsChanged();
        }
    }

    public MainViewModel()
    {
        _logTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, (_, _) =>
        { if (_logDirty) { _logDirty = false; RefreshLog(); } }, App.Current.Dispatcher);
        _logTimer.Start();
        _valueTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(50), DispatcherPriority.Background, (_, _) =>
        {
            var changed = false;
            foreach (var pair in _pendingValues.ToArray())
                if (_pendingValues.TryRemove(pair.Key, out var value) && _tree.Find(pair.Key) is { } node)
                {
                    node.Observed = value;
                    changed = true;
                }
            if (changed) _localSnapshotDirty = true;
            if (_localSnapshotDirty && _localServer is not null && Environment.TickCount64 >= _nextLocalSnapshotRefresh)
            {
                _localServer.UpdateTree(_tree.Root);
                _localSnapshotDirty = false;
                _nextLocalSnapshotRefresh = Environment.TickCount64 + 250;
            }
        }, App.Current.Dispatcher);
        _valueTimer.Start();
        _treeRefreshTimer = new DispatcherTimer(TimeSpan.FromSeconds(5), DispatcherPriority.Background,
            async (_, _) => await LoadTreeAsync(), App.Current.Dispatcher);
        ConnectCommand = new(ConnectAsync, () => !IsConnected && !IsConnecting);
        ReconnectCommand = new(ReconnectAsync, () => !IsConnecting && Uri.TryCreate(ManualUrl, UriKind.Absolute, out _));
        DisconnectCommand = new(DisconnectAsync, () => IsConnected);
        RefreshTreeCommand = new(LoadTreeAsync, () => IsConnected);
        ClearLogCommand = new(_history.Clear);
        PinCommand = new(ToggleSelectedPinAsync, () => SelectedNode is not null);
        RefreshDiscoveryCommand = new(_discovery.Refresh);
        SendCommand = new(SendCurrentAsync, () => IsConnected && IsSelectedNodeWritable && ArgumentEditors.Count > 0);
        _history.Changed += (_, _) => _logDirty = true;
        _query.PathChanged += (_, path) => App.Current.Dispatcher.BeginInvoke(() => _ = RefreshAfterPathChangedAsync(path));
        _query.WebSocketFaulted += (_, ex) => AddSystem(SystemLevel.Warning, $"WebSocket: {ex.Message}。OSC受信は継続します。");
        _query.BinaryPacketReceived += (_, bytes) => HandlePacket(bytes, null, null, DateTimeOffset.UtcNow, null, null);
        _udp.PacketReceived += (_, e) => HandlePacket(e.Packet, e.RemoteEndpoint.Port, e.LocalEndpoint.Port, e.Timestamp,
            e.RemoteEndpoint.Address.ToString(), e.LocalEndpoint.Address.Equals(IPAddress.Any) || e.LocalEndpoint.Address.Equals(IPAddress.IPv6Any) ? null : e.LocalEndpoint.Address.ToString());
        _udp.ReceiveFaulted += (_, ex) => AddSystem(SystemLevel.Warning, $"OSC受信: {ex.Message}");
        _discovery.ServiceFound += (_, service) => App.Current.Dispatcher.BeginInvoke(() =>
        {
            var existing = DiscoveredServices.FirstOrDefault(x => x.Identity.StableKey == service.Identity.StableKey);
            if (existing is not null) DiscoveredServices.Remove(existing);
            DiscoveredServices.Add(service);
            if (SelectedService is null && service.Identity.StableKey == _settings.LastServiceKey)
                SelectedService = service;
        });
        _discovery.DiscoveryWarning += (_, message) => AddSystem(SystemLevel.Warning, $"mDNS: {message}");
    }

    public async Task InitializeAsync()
    {
        _settings = await _settingsStore.LoadAsync();
        _settings.Panes ??= new PaneSettings();
        LogFilter = _settings.LogFilter;
        if (!string.IsNullOrWhiteSpace(_settings.LastManualEndpoint)) ManualUrl = _settings.LastManualEndpoint;
        Raise(nameof(LogFontSize));
        Raise(nameof(TypeDisplay));
        _treeRefreshTimer.Interval = TimeSpan.FromSeconds(Math.Clamp(_settings.PollingIntervalSeconds, 1, 300));
        _treeRefreshTimer.Start();
        _discovery.Start();
    }

    public async Task ConnectAsync()
    {
        if (IsConnecting) return;
        await _connectionGate.WaitAsync();
        var connectionToken = CancellationToken.None;
        ConnectionError = string.Empty;
        IsConnecting = true;
        CommandsChanged();
        try
        {
            await Dispatcher.Yield(DispatcherPriority.ContextIdle);
            if (!Uri.TryCreate(ManualUrl, UriKind.Absolute, out var endpoint) || (endpoint.Scheme != "http" && endpoint.Scheme != "https"))
                throw new ArgumentException("有効なHTTP/HTTPSのOSCQuery URLを入力してください。");

            var isVrChatService = IsVrChatEndpoint(endpoint);
            await DisconnectCoreAsync();
            _isVrChatService = isVrChatService;
            _connectionCts = new();
            connectionToken = _connectionCts.Token;
            _endpoint = endpoint; Status = "接続中…";
            _hostInfo = await _query.GetHostInfoAsync(endpoint, connectionToken);
            _currentServiceKey = ServiceStateKey(endpoint);
            _settings.LastServiceKey = SelectedService?.Identity.StableKey ?? _settings.LastServiceKey;
            _settings.LastManualEndpoint = endpoint.ToString();
            if (!_settings.Services.TryGetValue(_currentServiceKey, out var serviceState)) _settings.Services[_currentServiceKey] = serviceState = new ServiceState();
            _tree.SetCustomNodes(serviceState.CustomNodes);
            if (!string.Equals(_hostInfo.OscTransport, "UDP", StringComparison.OrdinalIgnoreCase)) throw new NotSupportedException($"OSC transport {_hostInfo.OscTransport} は未対応です。");
            _receiverTask = _udp.RunReceiverAsync(_settings.PublishToLan ? IPAddress.Any : IPAddress.Loopback, connectionToken);
            _localServer = new LocalOscQueryServer(() => _udp.ReceiveEndpoint?.Port ?? 0);
            _localServer.UpdateTree(_tree.Root);
            _localSnapshotDirty = false;
            _nextLocalSnapshotRefresh = Environment.TickCount64 + 250;
            await _localServer.StartAsync(_settings.PublishToLan, connectionToken);
            _discovery.Advertise(_localServer.Port, _udp.ReceiveEndpoint?.Port ?? 0, _settings.PublishToLan);
            await LoadTreeCoreAsync(endpoint, connectionToken);
            RestoreServiceState(serviceState);
            IsConnected = true; Status = $"接続済み: {_hostInfo.Name ?? endpoint.Host}  HTTP ✓  OSC受信:{_udp.ReceiveEndpoint?.Port}";
            AddSystem(SystemLevel.Info, $"{endpoint} に接続しました。OSC Receive Port: {_udp.ReceiveEndpoint?.Port}, Local OSCQuery HTTP Port: {_localServer.Port}.");
            if (_hostInfo.Extensions.GetValueOrDefault("PATH_CHANGED") || _hostInfo.Extensions.GetValueOrDefault("LISTEN"))
                _webSocketTask = _query.RunWebSocketAsync(endpoint, _hostInfo, connectionToken);
        }
        catch (OperationCanceledException) when (connectionToken.IsCancellationRequested)
        {
            await DisconnectCoreAsync();
        }
        catch (Exception ex)
        {
            var message = DescribeConnectionError(ex);
            await DisconnectCoreAsync();
            Status = "接続失敗";
            ConnectionError = message;
            AddSystem(SystemLevel.Error, $"接続失敗: {message}");
        }
        finally
        {
            IsConnecting = false;
            CommandsChanged();
            _connectionGate.Release();
        }
    }

    private async Task ReconnectAsync()
    {
        var reconnectUrl = _endpoint?.ToString() ?? ManualUrl;
        ManualUrl = reconnectUrl;
        await ConnectAsync();
    }

    private static string ServiceStateKey(Uri endpoint) => endpoint.AbsoluteUri.TrimEnd('/').ToUpperInvariant();

    private bool IsVrChatEndpoint(Uri endpoint)
    {
        if (SelectedService is { } selected &&
            SameEndpoint(selected.HttpEndpoint, endpoint) &&
            VrChatNodeMetadata.IsVrChatServiceName(selected.Identity.Name))
            return true;

        return DiscoveredServices.Any(service =>
            SameEndpoint(service.HttpEndpoint, endpoint) &&
            VrChatNodeMetadata.IsVrChatServiceName(service.Identity.Name));
    }

    private static bool SameEndpoint(Uri left, Uri right) =>
        Uri.Compare(
            left,
            right,
            UriComponents.SchemeAndServer | UriComponents.Path,
            UriFormat.SafeUnescaped,
            StringComparison.OrdinalIgnoreCase) == 0;

    private static string DescribeConnectionError(Exception exception) => exception switch
    {
        ArgumentException => exception.Message,
        HttpRequestException { StatusCode: { } statusCode } => $"OSCQueryサーバーがHTTP {(int)statusCode}を返しました。URLとサーバーの状態を確認してください。",
        HttpRequestException => $"OSCQueryサーバーに接続できませんでした。{exception.Message}",
        TaskCanceledException => "接続がタイムアウトしました。接続先が起動しているか確認してください。",
        System.Text.Json.JsonException => "接続先から受信したOSCQuery情報を解析できませんでした。",
        NotSupportedException => exception.Message,
        _ => $"接続中にエラーが発生しました。{exception.Message}"
    };

    private async Task LoadTreeAsync()
    {
        var endpoint = _endpoint;
        var connectionToken = _connectionCts?.Token ?? CancellationToken.None;
        if (endpoint is null || !connectionToken.CanBeCanceled || _treeRefreshInProgress) return;
        _treeRefreshInProgress = true;
        try
        {
            await LoadTreeCoreAsync(endpoint, connectionToken);
        }
        catch (OperationCanceledException) when (connectionToken.IsCancellationRequested) { }
        catch (OperationCanceledException) { AddSystem(SystemLevel.Warning, "ツリー取得がタイムアウトしました。次の更新で再試行します。"); }
        catch (Exception ex) { AddSystem(SystemLevel.Warning, $"ツリー取得失敗: {ex.Message}"); }
        finally { _treeRefreshInProgress = false; }
    }

    private async Task RefreshAfterPathChangedAsync(string path)
    {
        AddSystem(SystemLevel.Info, $"PATH_CHANGED {path}（アドレス空間を再取得します）");
        await LoadTreeAsync();
    }

    private async Task LoadTreeCoreAsync(Uri endpoint, CancellationToken cancellationToken)
    {
        var selectedPath = SelectedNode?.FullPath;
        var root = await _query.GetTreeAsync(endpoint, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        TreeUpdating?.Invoke(this, EventArgs.Empty);
        var structureChanged = _tree.ReplaceRemoteTree(root);
        if (_isVrChatService) VrChatNodeMetadata.ApplyDescriptions(_tree.Root);
        _localServer?.UpdateTree(_tree.Root);
        _localSnapshotDirty = false;
        if (structureChanged)
        {
            RefreshTree();
            ReconcilePinned();
            if (selectedPath is not null) SelectedNode = _tree.Find(selectedPath);
            TreeUpdated?.Invoke(this, EventArgs.Empty);
            return;
        }

        BuildEditors();
        BuildMetadata();
    }

    private async Task DisconnectAsync()
    {
        await _connectionGate.WaitAsync();
        try { await DisconnectCoreAsync(); }
        finally { _connectionGate.Release(); }
    }

    private async Task DisconnectCoreAsync()
    {
        var connectionCts = _connectionCts;
        _connectionCts = null;
        connectionCts?.Cancel();
        _udp.CloseSockets();
        _query.AbortWebSocket();
        if (IsConnected) AddSystem(SystemLevel.Info, "切断しました。");
        IsConnected = false; Status = "未接続"; _endpoint = null; _hostInfo = null; _isVrChatService = false; CommandsChanged();
        var server = _localServer;
        _localServer = null;
        _discovery.StopAdvertising();
        if (server is not null) await server.DisposeAsync();
        await AwaitConnectionTaskAsync(_receiverTask);
        await AwaitConnectionTaskAsync(_webSocketTask);
        _receiverTask = null;
        _webSocketTask = null;
        connectionCts?.Dispose();
    }

    private static async Task AwaitConnectionTaskAsync(Task? task)
    {
        if (task is null) return;
        try { await task; }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
    }

    private async Task ToggleSelectedPinAsync()
    {
        if (SelectedNode is null) return;
        SelectedNode.IsPinned = !SelectedNode.IsPinned;
        var existing = PinnedNodes.FirstOrDefault(item => ReferenceEquals(item.Node, SelectedNode));
        if (SelectedNode.IsPinned && existing is null) PinnedNodes.Add(CreatePinnedItem(SelectedNode));
        else if (!SelectedNode.IsPinned && existing is not null) { existing.Dispose(); PinnedNodes.Remove(existing); }
        await SaveSettingsAsync(); Raise(nameof(SelectedNode));
    }

    public async Task AddCustomNodeAsync(string path, string typeTag)
    {
        if (_currentServiceKey is null) return;
        path = NodeTree.Normalize(path);
        var state = _settings.Services[_currentServiceKey];
        state.CustomNodes.RemoveAll(x => x.FullPath == path);
        state.CustomNodes.Add(new(path, typeTag.TrimStart(',')));
        _tree.SetCustomNodes(state.CustomNodes);
        _localServer?.UpdateTree(_tree.Root);
        _localSnapshotDirty = false;
        RefreshTree();
        await SaveSettingsAsync();
    }

    public async Task SetSliderRangeRulesAsync(IEnumerable<SliderRangeRule> rules)
    {
        _settings.SliderRanges.Clear();
        _settings.SliderRanges.AddRange(rules.Select(rule => new SliderRangeRule
        {
            PathPrefix = NodeTree.Normalize(rule.PathPrefix),
            Minimum = rule.Minimum,
            Maximum = rule.Maximum,
            TypeTags = rule.TypeTags
        }));
        BuildEditors();
        ReconcilePinned();
        await SaveSettingsAsync();
    }

    public async Task TogglePublishedAsync(OscNode node)
    {
        node.IsPublished = !node.IsPublished;
        foreach (var descendant in node.Children.SelectMany(x => x.SelfAndDescendants())) descendant.IsPublished = node.IsPublished;
        if (_localServer is not null)
        {
            _localServer.UpdateTree(_tree.Root);
            _localSnapshotDirty = false;
            await _localServer.NotifyPathChangedAsync(node.FullPath);
        }
        RefreshTree();
    }

    public async Task TogglePinAsync(OscNode node)
    {
        SelectedNode = node;
        await ToggleSelectedPinAsync();
    }

    private void RestoreServiceState(ServiceState state)
    {
        foreach (var item in PinnedNodes) item.Dispose();
        PinnedNodes.Clear();
        foreach (var path in state.PinnedPaths)
        {
            var node = _tree.Find(path) ?? new OscNode { FullPath = path, IsAvailable = false };
            node.IsPinned = true; PinnedNodes.Add(CreatePinnedItem(node));
        }
        foreach (var path in state.ExpandedPaths)
            if (_tree.Find(path) is { } node) node.IsExpanded = true;
        if (state.SelectedPath is not null) SelectedNode = _tree.Find(state.SelectedPath);
    }

    private void ReconcilePinned()
    {
        if (PinnedNodes.Count == 0) return;
        var paths = PinnedNodes.Select(x => x.FullPath).ToArray();
        foreach (var item in PinnedNodes) item.Dispose();
        PinnedNodes.Clear();
        foreach (var path in paths)
        {
            var node = _tree.Find(path) ?? new OscNode { FullPath = path, IsAvailable = false };
            node.IsPinned = true;
            PinnedNodes.Add(CreatePinnedItem(node));
        }
    }

    private void BuildEditors()
    {
        ArgumentEditors.Clear();
        if (SelectedNode is null) return;
        foreach (var editor in CreateEditors(SelectedNode)) ArgumentEditors.Add(editor);
        SendCommand.RaiseCanExecuteChanged();
    }

    private void BuildMetadata()
    {
        SelectedMetadata.Clear();
        if (SelectedNode is not { } node) return;
        AddMetadata("TYPE", OscTypeFormatter.Format(node.TypeTag, TypeDisplay));
        AddMetadata("ACCESS", node.Access switch
        {
            OscAccess.None => "アクセス不可",
            OscAccess.Read => "読み取り",
            OscAccess.Write => "書き込み",
            OscAccess.Read | OscAccess.Write => "読み取り / 書き込み",
            { } access => $"{(int)access}",
            null => null
        });
        AddMetadata("RANGE", FormatJson(node.RangeMetadata));
        AddMetadata("UNIT", FormatJson(node.Unit));
        AddMetadata("DESCRIPTION", node.Description);
        foreach (var attribute in node.AdditionalAttributes.OrderBy(x => x.Key, StringComparer.Ordinal))
            AddMetadata(attribute.Key, FormatJson(attribute.Value));
    }

    private void AddMetadata(string label, string? value) =>
        SelectedMetadata.Add(new MetadataEntry(label, string.IsNullOrWhiteSpace(value) ? "未提供" : value));

    private static string? FormatJson(JsonNode? value)
    {
        if (value is null) return null;
        if (value is JsonValue scalar && scalar.TryGetValue<string>(out var text)) return text;
        return value.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
    }

    private ObservableCollection<ArgumentEditorViewModel> CreateEditors(OscNode node)
    {
        var result = new ObservableCollection<ArgumentEditorViewModel>();
        if (node.TypeTag is not { Length: > 0 } tags) return result;
        var values = node.Observed?.Values;
        var editorIndex = 0;
        for (var i = 0; i < tags.Length; i++)
        {
            var tag = tags[i]; if (tag is '[' or ']') continue;
            var range = editorIndex < node.Ranges.Count ? node.Ranges[editorIndex] : null;
            var hasRange = SliderRangeResolver.TryResolve(_settings.SliderRanges, node.FullPath, tag, out var minimum, out var maximum)
                || TryDouble(range?.Min, out minimum) && TryDouble(range?.Max, out maximum) && maximum > minimum;
            var editor = new ArgumentEditorViewModel
            {
                Index = editorIndex,
                TypeTag = tag,
                Description = _isVrChatService ? VrChatNodeMetadata.GetArgumentDescription(node.FullPath, editorIndex) : null,
                DisplayFormat = TypeDisplay,
                HasSlider = hasRange && tag is 'i' or 'f' or 'h' or 'd',
                Minimum = minimum,
                Maximum = hasRange ? maximum : 1
            };
            if (values is not null && editorIndex < values.Count) editor.SetValue(values[editorIndex]);
            result.Add(editor);
            editorIndex++;
        }
        return result;
    }

    private static bool TryDouble(object? value, out double result) => double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out result);
    private PinnedItemViewModel CreatePinnedItem(OscNode node) => new(node, CreateEditors(node), TypeDisplay);

    public Task SendIfImmediateAsync() => SendImmediately ? SendCurrentAsync() : Task.CompletedTask;
    private async Task SendCurrentAsync()
    {
        if (SelectedNode is null) return;
        await SendNodeAsync(SelectedNode, ArgumentEditors);
    }

    public Task SendPinnedAsync(PinnedItemViewModel item) => SendNodeAsync(item.Node, item.Editors);

    private async Task SendNodeAsync(OscNode node, IEnumerable<ArgumentEditorViewModel> editors)
    {
        if (!IsConnected || _hostInfo is null || _endpoint is null) return;
        if (node.Access is { } access && !access.HasFlag(OscAccess.Write)) return;
        var values = editors.Select(x => x.TryGetValue()).ToArray();
        if (values.Length == 0 || values.Any(x => x is null)) { AddSystem(SystemLevel.Warning, $"{node.FullPath}: 入力が不完全、または送信非対応の型です。"); return; }
        try
        {
            var host = _hostInfo.OscIp ?? _endpoint.Host;
            var cancellationToken = _connectionCts?.Token ?? CancellationToken.None;
            var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
            var address = addresses.FirstOrDefault(x => x.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) ?? addresses.First();
            var destination = new IPEndPoint(address, _hostInfo.OscPort ?? _endpoint.Port);
            var message = new OscMessage(node.FullPath, values.Select(x => x!).ToArray());
            var local = await _udp.SendAsync(message, destination, cancellationToken);
            _history.Add(id => new(id, DateTimeOffset.Now, HistoryKind.Osc, string.Empty, OscDirection.Sent, local.Port, destination.Port,
                message.Address, node.TypeTag, message.Arguments, SourceAddress: local.Address.Equals(IPAddress.Any) ? null : local.Address.ToString(), DestinationAddress: destination.Address.ToString()));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { AddSystem(SystemLevel.Error, $"OSC送信失敗: {ex.Message}"); }
    }

    private void HandlePacket(byte[] bytes, int? sourcePort, int? destinationPort, DateTimeOffset timestamp, string? sourceAddress, string? destinationAddress)
    {
        try
        {
            HandlePacket(OscCodec.Decode(bytes), sourcePort, destinationPort, timestamp, sourceAddress, destinationAddress);
        }
        catch (Exception ex) { AddSystem(SystemLevel.Warning, $"OSC decode: {ex.Message}"); }
    }

    private void HandlePacket(OscPacket packet, int? sourcePort, int? destinationPort, DateTimeOffset timestamp, string? sourceAddress, string? destinationAddress)
    {
        foreach (var message in Flatten(packet))
        {
            if (_tree.Find(message.Address) is not null) _pendingValues[message.Address] = new() { Values = message.Arguments, Origin = ValueOrigin.UdpReceived, UpdatedAt = timestamp };
            _history.Add(id => new(id, timestamp, HistoryKind.Osc, string.Empty, OscDirection.Received, sourcePort, destinationPort, message.Address, OscCodec.GetTypeTagString(message.Arguments), message.Arguments, SourceAddress: sourceAddress, DestinationAddress: destinationAddress));
        }
    }

    private static IEnumerable<OscMessage> Flatten(OscPacket packet) => packet switch
    { OscMessage message => [message], OscBundle bundle => bundle.Packets.SelectMany(Flatten), _ => [] };
    private void AddSystem(SystemLevel level, string message) => _history.Add(id => new(id, DateTimeOffset.Now, HistoryKind.System, message, Level: level));
    private void RefreshTree() { RootNodes.Clear(); foreach (var node in _tree.Search(TreeSearch)) RootNodes.Add(node); }
    private void RefreshLog()
    {
        if (IsPaused) return;
        var snapshot = _history.Snapshot();
        var filter = OSCQueryExplorer.Core.History.LogFilter.Parse(LogFilter);
        var canAppend = string.Equals(_renderedLogFilter, LogFilter, StringComparison.Ordinal)
            && _renderedTypeDisplay == TypeDisplay;

        if (!canAppend || snapshot.Count == 0 || _lastRenderedHistoryId == 0)
        {
            VisibleLog.ReplaceAll(snapshot.Where(entry => filter.IsMatch(entry, TypeDisplay)).Select(entry => new LogEntryViewModel(entry, TypeDisplay)));
        }
        else
        {
            var oldestId = snapshot[0].Id;
            VisibleLog.RemoveWhile(item => item.Entry.Id < oldestId);
            VisibleLog.AppendRange(snapshot
                .Where(entry => entry.Id > _lastRenderedHistoryId && filter.IsMatch(entry, TypeDisplay))
                .Select(entry => new LogEntryViewModel(entry, TypeDisplay)));
        }

        _renderedLogFilter = LogFilter;
        _renderedTypeDisplay = TypeDisplay;
        _lastRenderedHistoryId = snapshot.Count == 0 ? 0 : snapshot[^1].Id;
        RefreshParameterLog();
    }
    private void RefreshParameterLog()
    {
        if (SelectedNode is null) { ParameterLog.ReplaceAll([]); return; }
        var prefix = SelectedNode.FullPath == "/" ? "/" : SelectedNode.FullPath.TrimEnd('/') + "/";
        ParameterLog.ReplaceAll(VisibleLog.Where(item => item.Entry.Kind == HistoryKind.Osc &&
            (item.Entry.Address == SelectedNode.FullPath || item.Entry.Address?.StartsWith(prefix, StringComparison.Ordinal) == true)));
    }
    private void CommandsChanged() { ConnectCommand.RaiseCanExecuteChanged(); ReconnectCommand.RaiseCanExecuteChanged(); DisconnectCommand.RaiseCanExecuteChanged(); RefreshTreeCommand.RaiseCanExecuteChanged(); PinCommand.RaiseCanExecuteChanged(); SendCommand.RaiseCanExecuteChanged(); }
    private async Task SaveSettingsAsync()
    {
        _settings.LogFilter = LogFilter;
        if (_currentServiceKey is not null && _settings.Services.TryGetValue(_currentServiceKey, out var state))
        {
            state.PinnedPaths.Clear(); state.PinnedPaths.AddRange(PinnedNodes.Select(x => x.FullPath)); state.SelectedPath = SelectedNode?.FullPath;
            state.ExpandedPaths.Clear();
            foreach (var node in _tree.Root.SelfAndDescendants().Where(x => x.IsExpanded)) state.ExpandedPaths.Add(node.FullPath);
        }
        await _settingsStore.SaveAsync(_settings);
    }
    public async Task MovePinnedAsync(PinnedItemViewModel source, PinnedItemViewModel target, bool insertAfter)
    {
        var sourceIndex = PinnedNodes.IndexOf(source); var targetIndex = PinnedNodes.IndexOf(target);
        if (sourceIndex < 0 || targetIndex < 0 || sourceIndex == targetIndex) return;
        var destinationIndex = targetIndex + (insertAfter ? 1 : 0);
        if (sourceIndex < destinationIndex) destinationIndex--;
        if (sourceIndex == destinationIndex) return;
        PinnedNodes.Move(sourceIndex, destinationIndex); await SaveSettingsAsync();
    }
    public async ValueTask DisposeAsync() { _logTimer.Stop(); _valueTimer.Stop(); _treeRefreshTimer.Stop(); await DisconnectAsync(); _discovery.Dispose(); _updateChecker.Dispose(); await SaveSettingsAsync(); await _query.DisposeAsync(); await _udp.DisposeAsync(); }
}
