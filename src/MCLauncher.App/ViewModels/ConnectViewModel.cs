using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using MCLauncher.App.ViewModels.Mvvm;
using MCLauncher.Core.Connect;
using MCLauncher.Core.Models;

namespace MCLauncher.App.ViewModels;

/// <summary>联机方式。</summary>
public enum ConnectMethod
{
    Choose,
    Direct,
    Frp,
    Terracotta,
}

/// <summary>
/// 联机页：好友直连、樱花穿透、陶瓦联机三种方式，以及按地址直接加入。
/// </summary>
public sealed class ConnectViewModel : ViewModelBase
{
    private readonly LauncherStack _stack;
    private readonly Action<string, string, int> _launchServer;

    private readonly AsyncRelayCommand _refreshNetworkCommand;
    private readonly AsyncRelayCommand _detectPortCommand;
    private readonly AsyncRelayCommand _startDirectCommand;
    private readonly AsyncRelayCommand _stopDirectCommand;
    private readonly AsyncRelayCommand _joinDirectCommand;
    private readonly AsyncRelayCommand _startFrpCommand;
    private readonly AsyncRelayCommand _stopFrpCommand;
    private readonly AsyncRelayCommand _installTcCommand;
    private readonly AsyncRelayCommand _startTcHostCommand;
    private readonly AsyncRelayCommand _joinTcCommand;
    private readonly AsyncRelayCommand _stopTcCommand;
    private readonly RelayCommand _joinAddressCommand;

    private ConnectMethod _method = ConnectMethod.Choose;
    private InstalledVersionInfo? _selectedInstance;

    // ---- 直连 ----
    private string _lanPortText = string.Empty;
    private bool _useUpnp = true;
    private string _publicAddress = string.Empty;
    private bool _isDirectHosting;
    private string _directInvite = string.Empty;
    private string _directStatus = "先在游戏里选择「对局域网开放」，再开启房间。";
    private string _directConnectionsText = string.Empty;

    // ---- 樱花穿透 ----
    private string _frpAccessKey = string.Empty;
    private string _frpTunnelId = string.Empty;
    private string _frpStatusText = "尚未启动";
    private string _frpRemoteAddress = string.Empty;
    private bool _isFrpRunning;

    // ---- 陶瓦联机 ----
    private string _tcPlayerName = string.Empty;
    private string _tcStatusText = "尚未检测陶瓦联机工具。";
    private string _tcRoom = string.Empty;
    private string _tcUrl = string.Empty;
    private string _tcJoinCode = string.Empty;
    private bool _tcBinaryReady;
    private bool _isTcInstalling;
    private bool _isTcRunning;
    private bool _isTcBusy;
    private double _tcInstallProgress;
    private string _tcSpeedText = string.Empty;

    // ---- 加入 ----
    private string _joinInviteText = string.Empty;
    private string _joinAddressText = string.Empty;
    private string _joinStatusText = string.Empty;

    private string _networkStatus = "尚未扫描网络。";

    public ConnectViewModel(LauncherStack stack, Action<string, string, int> launchServer)
    {
        _stack = stack;
        _launchServer = launchServer;

        _frpAccessKey = stack.Config.Settings.FrpAccessKey ?? string.Empty;
        _frpTunnelId = stack.Config.Settings.FrpTunnelId ?? string.Empty;
        _tcPlayerName = stack.Config.Settings.TerracottaPlayerName
                        ?? stack.Accounts.Selected?.Name
                        ?? "MCLauncher";

        _refreshNetworkCommand = new AsyncRelayCommand(
            () => RefreshNetworkAsync(force: true),
            () => true,
            ex => NetworkStatus = "扫描网络失败：" + ex.Message);
        _detectPortCommand = new AsyncRelayCommand(
            () => DetectPortAsync(silent: false),
            () => SelectedInstance is not null,
            ex => DirectStatus = "检测端口失败：" + ex.Message);
        _startDirectCommand = new AsyncRelayCommand(
            StartDirectAsync,
            () => !IsDirectHosting && SelectedInstance is not null,
            ex => DirectStatus = ex.Message);
        _stopDirectCommand = new AsyncRelayCommand(
            StopDirectAsync,
            () => IsDirectHosting,
            ex => DirectStatus = ex.Message);
        _joinDirectCommand = new AsyncRelayCommand(
            JoinDirectAsync,
            () => !string.IsNullOrWhiteSpace(JoinInviteText),
            ex => JoinStatusText = ex.Message);

        _startFrpCommand = new AsyncRelayCommand(
            StartFrpAsync,
            () => !IsFrpRunning && !string.IsNullOrWhiteSpace(FrpAccessKey) && !string.IsNullOrWhiteSpace(FrpTunnelId),
            ex => FrpStatusText = ex.Message);
        _stopFrpCommand = new AsyncRelayCommand(
            StopFrpAsync,
            () => IsFrpRunning,
            ex => FrpStatusText = ex.Message);

        _installTcCommand = new AsyncRelayCommand(
            InstallTerracottaAsync,
            () => !IsTcInstalling && !IsTcRunning,
            ex => TcStatusText = ex.Message);
        _startTcHostCommand = new AsyncRelayCommand(
            StartTerracottaHostAsync,
            () => TcBinaryReady && !IsTcRunning && !IsTcInstalling,
            ex => TcStatusText = ex.Message);
        _joinTcCommand = new AsyncRelayCommand(
            JoinTerracottaAsync,
            () => TcBinaryReady && !IsTcRunning && !IsTcInstalling && !string.IsNullOrWhiteSpace(TcJoinCode),
            ex => TcStatusText = ex.Message);
        _stopTcCommand = new AsyncRelayCommand(
            StopTerracottaAsync,
            () => IsTcRunning || IsTcBusy,
            ex => TcStatusText = ex.Message);
        _joinAddressCommand = new RelayCommand(
            JoinByAddress,
            () => SelectedInstance is not null && !string.IsNullOrWhiteSpace(JoinAddressText));

        ShowDirectCommand = new RelayCommand(() => SwitchMethod(ConnectMethod.Direct));
        ShowFrpCommand = new RelayCommand(() => SwitchMethod(ConnectMethod.Frp));
        ShowTerracottaCommand = new RelayCommand(() => SwitchMethod(ConnectMethod.Terracotta));
        BackCommand = new RelayCommand(() => SwitchMethod(ConnectMethod.Choose));
        CopyDirectInviteCommand = new RelayCommand(() => Copy(DirectInvite));
        CopyFrpAddressCommand = new RelayCommand(() => Copy(FrpRemoteAddress));
        CopyTcRoomCommand = new RelayCommand(() => Copy(TcRoom));
        CopyTcUrlCommand = new RelayCommand(() => Copy(TcUrl));

        _stack.DirectConnect.StateChanged += (_, state) => UiThread.Post(() => ApplyDirectState(state));
        _stack.Frp.StateChanged += (_, state) => UiThread.Post(() => ApplyFrpState(state));
        _stack.Frp.LogReceived += (_, line) => UiThread.Post(() => AppendFrpLog(line));
        _stack.Terracotta.StateChanged += (_, state) => UiThread.Post(() => ApplyTerracottaState(state));
        _stack.Changed += (_, _) => UiThread.Post(RefreshInstances);

        RefreshInstances();
        ApplyDirectState(_stack.DirectConnect.State);
        ApplyFrpState(_stack.Frp.State);
    }

    // ---------------------------------------------------------------- 导航

    public bool IsChoosePage => _method == ConnectMethod.Choose;

    public bool IsDirectPage => _method == ConnectMethod.Direct;

    public bool IsFrpPage => _method == ConnectMethod.Frp;

    public bool IsTerracottaPage => _method == ConnectMethod.Terracotta;

    public bool IsMethodPage => _method != ConnectMethod.Choose;

    public string HeaderTitle => _method switch
    {
        ConnectMethod.Direct => "好友直连",
        ConnectMethod.Frp => "樱花穿透",
        ConnectMethod.Terracotta => "陶瓦联机",
        _ => "联机",
    };

    public string HeaderSubtitle => _method switch
    {
        ConnectMethod.Direct => "公网 IPv6 / 路由器 UPnP / 同一局域网，不需要第三方账号。",
        ConnectMethod.Frp => "用樱花穿透官方 frpc 把本地世界映射到公网，需要自备访问密钥。",
        ConnectMethod.Terracotta => "独立开源联机工具（AGPL-3.0），用房间码和好友互联。",
        _ => "选择一种联机方式，按指引与好友一起游玩。",
    };

    public ICommand ShowDirectCommand { get; }

    public ICommand ShowFrpCommand { get; }

    public ICommand ShowTerracottaCommand { get; }

    public ICommand BackCommand { get; }

    // ---------------------------------------------------------------- 实例

    public ObservableCollection<InstalledVersionInfo> Instances { get; } = new();

    public InstalledVersionInfo? SelectedInstance
    {
        get => _selectedInstance;
        set
        {
            if (!SetProperty(ref _selectedInstance, value))
            {
                return;
            }

            OnPropertyChanged(nameof(SelectedInstanceSummary));
            OnPropertyChanged(nameof(FrpLocalPortHint));
            RaiseCommandStates();
            _ = DetectPortAsync(silent: true);
        }
    }

    public string SelectedInstanceSummary => SelectedInstance is null
        ? "还没有选择实例。"
        : SelectedInstance.Subtitle;

    // ---------------------------------------------------------------- 直连

    public string LanPortText
    {
        get => _lanPortText;
        set
        {
            if (SetProperty(ref _lanPortText, value))
            {
                OnPropertyChanged(nameof(FrpLocalPortHint));
            }
        }
    }

    public bool UseUpnp
    {
        get => _useUpnp;
        set => SetProperty(ref _useUpnp, value);
    }

    public string PublicAddress
    {
        get => _publicAddress;
        set => SetProperty(ref _publicAddress, value);
    }

    public bool IsDirectHosting
    {
        get => _isDirectHosting;
        private set
        {
            if (SetProperty(ref _isDirectHosting, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public string DirectInvite
    {
        get => _directInvite;
        private set => SetProperty(ref _directInvite, value);
    }

    public string DirectStatus
    {
        get => _directStatus;
        private set => SetProperty(ref _directStatus, value);
    }

    public string DirectConnectionsText
    {
        get => _directConnectionsText;
        private set => SetProperty(ref _directConnectionsText, value);
    }

    public ObservableCollection<string> DirectEndpoints { get; } = new();

    public ObservableCollection<string> DirectMessages { get; } = new();

    public ICommand RefreshNetworkCommand => _refreshNetworkCommand;

    public ICommand DetectPortCommand => _detectPortCommand;

    public ICommand StartDirectCommand => _startDirectCommand;

    public ICommand StopDirectCommand => _stopDirectCommand;

    public ICommand CopyDirectInviteCommand { get; }

    public string NetworkStatus
    {
        get => _networkStatus;
        private set => SetProperty(ref _networkStatus, value);
    }

    public ObservableCollection<string> NetworkLines { get; } = new();

    // ---------------------------------------------------------------- 樱花穿透

    public string FrpAccessKey
    {
        get => _frpAccessKey;
        set
        {
            if (SetProperty(ref _frpAccessKey, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public string FrpTunnelId
    {
        get => _frpTunnelId;
        set
        {
            if (SetProperty(ref _frpTunnelId, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public string FrpStatusText
    {
        get => _frpStatusText;
        private set => SetProperty(ref _frpStatusText, value);
    }

    public string FrpRemoteAddress
    {
        get => _frpRemoteAddress;
        private set => SetProperty(ref _frpRemoteAddress, value);
    }

    public bool IsFrpRunning
    {
        get => _isFrpRunning;
        private set
        {
            if (SetProperty(ref _isFrpRunning, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public string FrpLocalPortHint => string.IsNullOrWhiteSpace(LanPortText)
        ? "先在 natfrp.com 创建 TCP 隧道，本地端口填游戏里「对局域网开放」显示的端口。"
        : $"隧道本地端口应填 {LanPortText}（游戏里显示的局域网端口）。";

    public ObservableCollection<string> FrpLogs { get; } = new();

    public ICommand StartFrpCommand => _startFrpCommand;

    public ICommand StopFrpCommand => _stopFrpCommand;

    public ICommand CopyFrpAddressCommand { get; }

    // ---------------------------------------------------------------- 陶瓦联机

    public string TcPlayerName
    {
        get => _tcPlayerName;
        set => SetProperty(ref _tcPlayerName, value);
    }

    public string TcStatusText
    {
        get => _tcStatusText;
        private set => SetProperty(ref _tcStatusText, value);
    }

    public string TcRoom
    {
        get => _tcRoom;
        private set => SetProperty(ref _tcRoom, value);
    }

    public string TcUrl
    {
        get => _tcUrl;
        private set => SetProperty(ref _tcUrl, value);
    }

    public string TcJoinCode
    {
        get => _tcJoinCode;
        set
        {
            if (SetProperty(ref _tcJoinCode, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public bool TcBinaryReady
    {
        get => _tcBinaryReady;
        private set
        {
            if (SetProperty(ref _tcBinaryReady, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public bool IsTcInstalling
    {
        get => _isTcInstalling;
        private set
        {
            if (SetProperty(ref _isTcInstalling, value))
            {
                OnPropertyChanged(nameof(IsTcSpeedVisible));
                RaiseCommandStates();
            }
        }
    }

    public bool IsTcRunning
    {
        get => _isTcRunning;
        private set
        {
            if (SetProperty(ref _isTcRunning, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public bool IsTcBusy
    {
        get => _isTcBusy;
        private set
        {
            if (SetProperty(ref _isTcBusy, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public double TcInstallProgress
    {
        get => _tcInstallProgress;
        private set => SetProperty(ref _tcInstallProgress, value);
    }

    public string TcSpeedText
    {
        get => _tcSpeedText;
        private set
        {
            if (SetProperty(ref _tcSpeedText, value))
            {
                OnPropertyChanged(nameof(IsTcSpeedVisible));
            }
        }
    }

    public bool IsTcSpeedVisible => IsTcInstalling && !string.IsNullOrWhiteSpace(TcSpeedText);

    public ICommand InstallTcCommand => _installTcCommand;

    public ICommand StartTcHostCommand => _startTcHostCommand;

    public ICommand JoinTcCommand => _joinTcCommand;

    public ICommand StopTcCommand => _stopTcCommand;

    public ICommand CopyTcRoomCommand { get; }

    public ICommand CopyTcUrlCommand { get; }

    // ---------------------------------------------------------------- 加入

    public string JoinInviteText
    {
        get => _joinInviteText;
        set
        {
            if (SetProperty(ref _joinInviteText, value))
            {
                _joinDirectCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string JoinAddressText
    {
        get => _joinAddressText;
        set
        {
            if (SetProperty(ref _joinAddressText, value))
            {
                _joinAddressCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string JoinStatusText
    {
        get => _joinStatusText;
        private set => SetProperty(ref _joinStatusText, value);
    }

    public ICommand JoinDirectCommand => _joinDirectCommand;

    public ICommand JoinAddressCommand => _joinAddressCommand;

    // ---------------------------------------------------------------- 实现

    private void SwitchMethod(ConnectMethod method)
    {
        if (_method == method)
        {
            return;
        }

        _method = method;
        OnPropertyChanged(nameof(IsChoosePage));
        OnPropertyChanged(nameof(IsDirectPage));
        OnPropertyChanged(nameof(IsFrpPage));
        OnPropertyChanged(nameof(IsTerracottaPage));
        OnPropertyChanged(nameof(IsMethodPage));
        OnPropertyChanged(nameof(HeaderTitle));
        OnPropertyChanged(nameof(HeaderSubtitle));

        if (method == ConnectMethod.Direct)
        {
            _ = RefreshNetworkAsync(force: false);
            _ = DetectPortAsync(silent: true);
        }
        else if (method == ConnectMethod.Frp)
        {
            _ = DetectPortAsync(silent: true);
        }
        else if (method == ConnectMethod.Terracotta)
        {
            _ = CheckTerracottaBinaryAsync();
        }
    }

    private void RefreshInstances()
    {
        var previousId = SelectedInstance?.Id ?? _stack.Config.Settings.SelectedInstanceId;
        var all = _stack.GetInstalledVersions();

        Instances.Clear();
        foreach (var info in all)
        {
            Instances.Add(info);
        }

        if (Instances.Count == 0)
        {
            SelectedInstance = null;
            return;
        }

        SelectedInstance = Instances.FirstOrDefault(item =>
                             string.Equals(item.Id, previousId, StringComparison.OrdinalIgnoreCase))
                         ?? Instances.FirstOrDefault(item =>
                             string.Equals(item.Id, _stack.Config.Settings.SelectedInstanceId, StringComparison.OrdinalIgnoreCase))
                         ?? Instances[0];
    }

    private async Task RefreshNetworkAsync(bool force)
    {
        NetworkStatus = "正在扫描本机地址与 UPnP 网关…";
        NetworkLines.Clear();

        try
        {
            var network = await _stack.DirectConnect.InspectNetworkAsync(force).ConfigureAwait(true);

            foreach (var address in network.Addresses)
            {
                NetworkLines.Add(address.Text);
            }

            foreach (var gateway in network.Gateways)
            {
                var external = string.IsNullOrWhiteSpace(gateway.ExternalAddress) ? "未知" : gateway.ExternalAddress;
                NetworkLines.Add($"UPnP 网关 {gateway.ControlHost}：WAN {external}");
            }

            foreach (var message in network.Messages)
            {
                NetworkLines.Add(message);
            }

            NetworkStatus = network.Addresses.Count == 0
                ? "没有发现可用的本机地址。"
                : $"已发现 {network.Addresses.Count} 个本机地址、{network.Gateways.Count} 个 UPnP 网关。";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            NetworkStatus = "扫描网络失败：" + ex.Message;
        }
    }

    private async Task DetectPortAsync(bool silent)
    {
        var instance = SelectedInstance;
        if (instance is null)
        {
            LanPortText = string.Empty;
            return;
        }

        try
        {
            var directory = _stack.GetInstanceDirectory(instance.Id, _stack.IsIsolated(instance.Id));
            var port = await _stack.DirectConnect.DetectLanPortAsync(directory).ConfigureAwait(true);

            if (port is { } value)
            {
                LanPortText = value.ToString();
                if (!silent)
                {
                    DirectStatus = $"已从游戏日志里检测到局域网端口 {value}。";
                }
            }
            else
            {
                if (!silent)
                {
                    DirectStatus = "日志里没有找到局域网端口。请先在游戏里「对局域网开放」，再刷新或手动填写。";
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (!silent)
            {
                DirectStatus = "读取游戏日志失败：" + ex.Message;
            }
        }
    }

    private async Task StartDirectAsync()
    {
        var instance = SelectedInstance;
        if (instance is null)
        {
            DirectStatus = "请先选择要联机的实例。";
            return;
        }

        int? port = null;
        if (!string.IsNullOrWhiteSpace(LanPortText))
        {
            if (!int.TryParse(LanPortText.Trim(), out var parsed) || !DirectProtocol.IsValidPort(parsed))
            {
                DirectStatus = "端口必须是 1–65535 的整数。";
                return;
            }

            port = parsed;
        }

        var directory = _stack.GetInstanceDirectory(instance.Id, _stack.IsIsolated(instance.Id));
        DirectStatus = "正在开启房间…";

        var state = await _stack.DirectConnect.StartHostAsync(new DirectHostRequest
        {
            InstanceId = instance.Id,
            InstanceName = instance.Display,
            InstanceDirectory = directory,
            MinecraftVersion = instance.VersionText,
            Loader = instance.LoaderName,
            LoaderVersion = instance.LoaderVersion,
            Port = port,
            UseUpnp = UseUpnp,
            PublicAddress = string.IsNullOrWhiteSpace(PublicAddress) ? null : PublicAddress.Trim(),
        }).ConfigureAwait(true);

        ApplyDirectState(state);
    }

    private async Task StopDirectAsync()
    {
        DirectStatus = "正在关闭房间…";
        var state = await _stack.DirectConnect.StopHostAsync().ConfigureAwait(true);
        ApplyDirectState(state);
    }

    private void ApplyDirectState(DirectHostState state)
    {
        IsDirectHosting = state.Active;
        DirectInvite = state.Invite ?? string.Empty;
        DirectConnectionsText = state.Active ? $"已连接 {state.Connections} 个" : string.Empty;

        DirectEndpoints.Clear();
        foreach (var endpoint in state.Endpoints)
        {
            DirectEndpoints.Add($"{endpoint.Address}（{endpoint.KindText}）");
        }

        DirectMessages.Clear();
        foreach (var message in state.Messages)
        {
            DirectMessages.Add(message);
        }

        if (state.Active)
        {
            DirectStatus = $"房间已开启：游戏端口 {state.LocalPort}，对外端口 {state.ExposedPort}。";
        }
        else if (state.Messages.Count > 0)
        {
            DirectStatus = state.Messages[0];
        }
    }

    private async Task JoinDirectAsync()
    {
        JoinStatusText = "正在解析邀请并探测地址…";

        DirectJoinResult result;
        try
        {
            result = await _stack.DirectConnect.ResolveInvitationAsync(JoinInviteText).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            JoinStatusText = "邀请无效：" + ex.Message;
            return;
        }

        if (result.Endpoint is null)
        {
            JoinStatusText = "没有可达的好友地址。请确认房主已开启房间，并检查双方网络。"
                             + (result.Failures.Count > 0 ? Environment.NewLine + string.Join(Environment.NewLine, result.Failures) : string.Empty);
            return;
        }

        var instance = FindMatchingInstance(result.Invitation);
        if (instance is null)
        {
            JoinStatusText = $"没有找到与房主匹配的实例（Minecraft {result.Invitation.MinecraftVersion}，"
                             + $"{result.Invitation.LoaderText}）。请先安装同版本实例。";
            return;
        }

        var endpoint = result.Endpoint;
        LaunchJoin(instance, endpoint.Host, endpoint.Port, $"联机 · {result.Invitation.Name}");
        JoinStatusText = $"正在启动 {instance.Display} 并加入 {endpoint.Address}…";
    }

    private InstalledVersionInfo? FindMatchingInstance(DirectInvitation invitation)
    {
        IEnumerable<InstalledVersionInfo> candidates = Instances.Where(item =>
            string.Equals(item.VersionText, invitation.MinecraftVersion, StringComparison.OrdinalIgnoreCase)
            && LoaderMatches(item, invitation));

        var preferred = SelectedInstance;
        if (preferred is not null && candidates.Contains(preferred))
        {
            return preferred;
        }

        return candidates.FirstOrDefault();
    }

    private static bool LoaderMatches(InstalledVersionInfo instance, DirectInvitation invitation)
    {
        var loader = instance.LoaderName.Equals("原版", StringComparison.OrdinalIgnoreCase)
            ? null
            : instance.LoaderName.ToLowerInvariant();

        return string.Equals(loader, invitation.Loader, StringComparison.OrdinalIgnoreCase);
    }

    private async Task StartFrpAsync()
    {
        _stack.Config.Settings.FrpAccessKey = FrpAccessKey.Trim();
        _stack.Config.Settings.FrpTunnelId = FrpTunnelId.Trim();
        _stack.Config.Save();

        FrpLogs.Clear();
        FrpStatusText = "正在准备 frpc…";

        await _stack.Frp.StartAsync(FrpAccessKey, FrpTunnelId).ConfigureAwait(true);
        ApplyFrpState(_stack.Frp.State);
    }

    private async Task StopFrpAsync()
    {
        FrpStatusText = "正在停止 frpc…";
        var state = await _stack.Frp.StopAsync().ConfigureAwait(true);
        ApplyFrpState(state);
    }

    private void ApplyFrpState(FrpState state)
    {
        IsFrpRunning = state.Status is FrpStatusKind.Starting or FrpStatusKind.Running;
        FrpRemoteAddress = state.RemoteAddress ?? string.Empty;
        FrpStatusText = state.Message;
    }

    private void AppendFrpLog(string line)
    {
        FrpLogs.Add(line);
        while (FrpLogs.Count > 200)
        {
            FrpLogs.RemoveAt(0);
        }
    }

    private async Task CheckTerracottaBinaryAsync()
    {
        TcStatusText = "正在校验陶瓦联机工具…";

        var ready = await Task.Run(() => _stack.Terracotta.IsBinaryReady()).ConfigureAwait(true);
        TcBinaryReady = ready;
        TcStatusText = ready
            ? "陶瓦联机工具已就绪。房主先在游戏里「对局域网开放」，再创建房间。"
            : "尚未下载陶瓦联机官方工具（独立开源项目，AGPL-3.0）。";
    }

    private async Task InstallTerracottaAsync()
    {
        IsTcInstalling = true;
        TcInstallProgress = 0;
        TcSpeedText = string.Empty;
        TcStatusText = "正在下载陶瓦联机官方工具…";

        var progress = new Progress<TerracottaInstallProgress>(value =>
        {
            TcInstallProgress = value.Fraction;
            TcSpeedText = FormatSpeed(value.BytesPerSecond);
        });

        try
        {
            await _stack.Terracotta.InstallAsync(progress).ConfigureAwait(true);
            TcBinaryReady = true;
            TcStatusText = "陶瓦联机工具已下载并通过 SHA-256 校验。";
        }
        finally
        {
            IsTcInstalling = false;
            TcSpeedText = string.Empty;
        }
    }

    private static string FormatSpeed(double bytesPerSecond)
    {
        if (bytesPerSecond < 1024)
        {
            return string.Empty;
        }

        return bytesPerSecond < 1024 * 1024
            ? $"{bytesPerSecond / 1024:0} KB/s"
            : $"{bytesPerSecond / 1024 / 1024:0.0} MB/s";
    }

    private async Task StartTerracottaHostAsync()
    {
        IsTcBusy = true;
        TcRoom = string.Empty;
        TcUrl = string.Empty;
        TcStatusText = "正在创建陶瓦房间…";

        try
        {
            var playerName = ResolvePlayerName();
            _stack.Config.Settings.TerracottaPlayerName = playerName;
            _stack.Config.Save();

            var state = await _stack.Terracotta.StartHostAsync(playerName).ConfigureAwait(true);
            ApplyTerracottaState(state);

            if (!string.IsNullOrWhiteSpace(state.Room))
            {
                TcStatusText = $"房间已创建：{state.Room}。把房间码发给好友；房主要保持游戏和启动器运行。";
            }
        }
        finally
        {
            IsTcBusy = false;
        }
    }

    private async Task JoinTerracottaAsync()
    {
        IsTcBusy = true;
        TcRoom = string.Empty;
        TcUrl = string.Empty;
        TcStatusText = "正在加入陶瓦房间…";

        try
        {
            var playerName = ResolvePlayerName();
            var state = await _stack.Terracotta.StartJoinAsync(TcJoinCode, playerName).ConfigureAwait(true);
            ApplyTerracottaState(state);

            if (string.IsNullOrWhiteSpace(state.Url))
            {
                return;
            }

            if (!DirectProtocol.TryParseAddress(state.Url!, out var host, out var port))
            {
                TcStatusText = $"已连接，但引擎给出的地址无法识别：{state.Url}";
                return;
            }

            var instance = SelectedInstance;
            if (instance is null)
            {
                TcStatusText = $"已连接（{state.Url}），请选择实例后手动加入。";
                return;
            }

            LaunchJoin(instance, host, port, $"陶瓦 · {TcJoinCode}");
            TcStatusText = $"已连接，正在启动 {instance.Display} 并加入 {state.Url}…";
        }
        finally
        {
            IsTcBusy = false;
        }
    }

    private async Task StopTerracottaAsync()
    {
        TcStatusText = "正在关闭陶瓦房间…";
        var state = await _stack.Terracotta.StopAsync().ConfigureAwait(true);
        ApplyTerracottaState(state);
        TcRoom = string.Empty;
        TcUrl = string.Empty;
        TcStatusText = "陶瓦房间已关闭。";
    }

    private void ApplyTerracottaState(TerracottaState state)
    {
        IsTcRunning = state.Phase is TerracottaPhase.Starting or TerracottaPhase.Hosting
            or TerracottaPhase.Joining or TerracottaPhase.Ready;

        if (!string.IsNullOrWhiteSpace(state.Room))
        {
            TcRoom = state.Room;
        }

        if (!string.IsNullOrWhiteSpace(state.Url))
        {
            TcUrl = state.Url;
        }

        if (!string.IsNullOrWhiteSpace(state.Error))
        {
            TcStatusText = "陶瓦联机失败：" + state.Error;
        }
        else if (!string.IsNullOrWhiteSpace(state.Message))
        {
            TcStatusText = state.Message!;
        }
        else if (state.Phase == TerracottaPhase.Hosting)
        {
            TcStatusText = "正在等待房间号…";
        }
        else if (state.Phase == TerracottaPhase.Joining)
        {
            TcStatusText = "正在等待连接就绪…";
        }
        else if (state.Phase == TerracottaPhase.Ready)
        {
            TcStatusText = state.Room is not null && state.Url is null
                ? $"房间已创建：{state.Room}"
                : $"已连接：{state.Url}";
        }
    }

    private string ResolvePlayerName()
    {
        var configured = TerracottaPlayerName();
        return string.IsNullOrWhiteSpace(configured) ? "MCLauncher" : configured;
    }

    private string TerracottaPlayerName()
    {
        if (!string.IsNullOrWhiteSpace(TcPlayerName))
        {
            return TcPlayerName.Trim();
        }

        return _stack.Accounts.Selected?.Name ?? string.Empty;
    }

    private void JoinByAddress()
    {
        if (!DirectProtocol.TryParseAddress(JoinAddressText, out var host, out var port))
        {
            JoinStatusText = "地址格式无效，请填写 host 或 host:port，例如 1.2.3.4:25565。";
            return;
        }

        var instance = SelectedInstance;
        if (instance is null)
        {
            JoinStatusText = "请先选择实例。";
            return;
        }

        LaunchJoin(instance, host, port, $"联机 · {instance.Display}");
        JoinStatusText = $"正在启动 {instance.Display} 并加入 {DirectProtocol.FormatAddress(host, port)}…";
    }

    private void LaunchJoin(InstalledVersionInfo instance, string host, int port, string serverName)
    {
        var directory = _stack.GetInstanceDirectory(instance.Id, _stack.IsIsolated(instance.Id));
        var address = DirectProtocol.FormatAddress(host, port);

        try
        {
            var servers = _stack.ServerList.Load(directory).ToList();
            var existing = servers.FirstOrDefault(server =>
                string.Equals(server.Address, address, StringComparison.OrdinalIgnoreCase));

            if (existing is null)
            {
                servers.Insert(0, new MinecraftServerEntry { Name = serverName, Address = address });
            }
            else
            {
                existing.Name = serverName;
            }

            _stack.ServerList.Save(directory, servers);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _stack.Log.Warn("写入服务器列表失败", ex);
        }

        _launchServer(instance.Id, host, port);
    }

    private void Copy(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        try
        {
            Clipboard.SetText(text);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            _stack.Log.Warn("写入剪贴板失败", ex);
        }
    }

    private void RaiseCommandStates()
    {
        _refreshNetworkCommand.RaiseCanExecuteChanged();
        _detectPortCommand.RaiseCanExecuteChanged();
        _startDirectCommand.RaiseCanExecuteChanged();
        _stopDirectCommand.RaiseCanExecuteChanged();
        _joinDirectCommand.RaiseCanExecuteChanged();
        _startFrpCommand.RaiseCanExecuteChanged();
        _stopFrpCommand.RaiseCanExecuteChanged();
        _installTcCommand.RaiseCanExecuteChanged();
        _startTcHostCommand.RaiseCanExecuteChanged();
        _joinTcCommand.RaiseCanExecuteChanged();
        _stopTcCommand.RaiseCanExecuteChanged();
        _joinAddressCommand.RaiseCanExecuteChanged();
    }
}
