using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using MCLauncher.App.ViewModels.Mvvm;
using MCLauncher.Core.Models;
using MCLauncher.Core.Services.Auth;

namespace MCLauncher.App.ViewModels;

/// <summary>账号列表项。</summary>
public sealed class AccountItem : ViewModelBase
{
    private bool _isSelected;

    public required Account Account { get; init; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public string Display =>
        $"{Account.Name}　{(Account.IsMicrosoft ? "微软正版" : "离线")}　{Account.Uuid[..Math.Min(8, Account.Uuid.Length)]}…";
}

/// <summary>账号页：离线账号管理与微软正版登录。</summary>
public sealed class AccountsViewModel : ViewModelBase
{
    private readonly LauncherStack _stack;
    private readonly AsyncRelayCommand _loginMsCommand;
    private readonly AsyncRelayCommand _loginDeviceCodeCommand;

    private string _newAccountName = string.Empty;
    private string _statusText = "离线账号无需密码，UUID 由玩家名按协议派生。";
    private bool _isBusy;
    private CancellationTokenSource? _loginCts;
    private string? _deviceCodeText;
    private string? _deviceCodeUri;

    public AccountsViewModel(LauncherStack stack)
    {
        _stack = stack;

        AddOfflineCommand = new RelayCommand(AddOffline);
        RemoveCommand = new RelayCommand<AccountItem>(Remove);
        SelectCommand = new RelayCommand<AccountItem>(Select);
        RefreshCommand = new RelayCommand(Refresh);
        CancelLoginCommand = new RelayCommand(() => _loginCts?.Cancel());
        OpenDeviceCodePageCommand = new RelayCommand(OpenDeviceCodePage);

        _loginMsCommand = new AsyncRelayCommand(LoginMicrosoftAsync, () => !IsBusy && HasMsClientId);
        LoginMicrosoftCommand = _loginMsCommand;

        _loginDeviceCodeCommand = new AsyncRelayCommand(LoginByDeviceCodeAsync, () => !IsBusy && HasMsClientId);
        LoginByDeviceCodeCommand = _loginDeviceCodeCommand;

        _stack.Changed += (_, _) => UiThread.Post(Refresh);
        Refresh();
    }

    public ObservableCollection<AccountItem> Accounts { get; } = new();

    public string NewAccountName
    {
        get => _newAccountName;
        set => SetProperty(ref _newAccountName, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                _loginMsCommand.RaiseCanExecuteChanged();
                _loginDeviceCodeCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>设备码登录时展示给用户的短码；未在设备码流程中时为空。</summary>
    public string DeviceCodeText
    {
        get => _deviceCodeText ?? string.Empty;
        private set
        {
            _deviceCodeText = string.IsNullOrWhiteSpace(value) ? null : value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasDeviceCode));
        }
    }

    public string DeviceCodeUri
    {
        get => _deviceCodeUri ?? string.Empty;
        private set
        {
            _deviceCodeUri = string.IsNullOrWhiteSpace(value) ? null : value;
            OnPropertyChanged();
        }
    }

    public bool HasDeviceCode => _deviceCodeText is not null;

    /// <summary>是否已配置微软客户端 ID（未配置时界面给出引导，而不是报错）。</summary>
    public bool HasMsClientId => _stack.Config.ResolveMsClientId() is not null;

    public string MsClientIdHint => HasMsClientId
        ? "已配置客户端 ID，可进行微软正版登录。"
        : "尚未配置微软客户端 ID：请到「设置」页填入 Azure 应用客户端 ID 后再登录。";

    /// <summary>回调地址的注册指引。登录报 redirect_uri 错误时，问题几乎都出在这里。</summary>
    public string MsRedirectHint => _stack.MicrosoftAuth.DescribeRedirectSetup();

    public string SelectedAccountText => _stack.Accounts.Selected is { } account
        ? $"当前账号：{account}"
        : "当前没有可用账号（启动游戏时会自动创建一个离线账号）";

    public ICommand AddOfflineCommand { get; }

    public ICommand RemoveCommand { get; }

    public ICommand SelectCommand { get; }

    public ICommand RefreshCommand { get; }

    public ICommand LoginMicrosoftCommand { get; }

    /// <summary>设备码登录：不需要注册任何回调地址，适合 Azure 回调地址配不对的情况。</summary>
    public ICommand LoginByDeviceCodeCommand { get; }

    public ICommand OpenDeviceCodePageCommand { get; }

    public ICommand CancelLoginCommand { get; }

    public void Refresh()
    {
        var selectedUuid = _stack.Accounts.Selected?.Uuid;

        Accounts.Clear();
        foreach (var account in _stack.Accounts.Accounts)
        {
            Accounts.Add(new AccountItem
            {
                Account = account,
                IsSelected = account.Uuid == selectedUuid,
            });
        }

        OnPropertyChanged(nameof(HasMsClientId));
        OnPropertyChanged(nameof(MsClientIdHint));
        OnPropertyChanged(nameof(MsRedirectHint));
        OnPropertyChanged(nameof(SelectedAccountText));
        _loginMsCommand.RaiseCanExecuteChanged();
    }

    private void AddOffline()
    {
        try
        {
            var account = _stack.Accounts.AddOffline(NewAccountName);
            NewAccountName = string.Empty;
            _stack.Config.Save();
            StatusText = $"已添加离线账号：{account.Name}";
            Refresh();
            _stack.RaiseChanged();
        }
        catch (Exception ex)
        {
            StatusText = "添加失败：" + ex.Message;
        }
    }

    private void Remove(AccountItem? item)
    {
        if (item is null)
        {
            return;
        }

        if (_stack.Accounts.Remove(item.Account))
        {
            _stack.Config.Save();
            StatusText = $"已删除账号：{item.Account.Name}";
            Refresh();
            _stack.RaiseChanged();
        }
    }

    private void Select(AccountItem? item)
    {
        if (item is null)
        {
            return;
        }

        _stack.Accounts.Select(item.Account);
        _stack.Config.Save();
        StatusText = $"已切换账号：{item.Account}";
        Refresh();
        _stack.RaiseChanged();
    }

    /// <summary>微软正版登录：打开系统浏览器完成授权，回调由本地回环服务器接收。</summary>
    private async Task LoginMicrosoftAsync()
    {
        IsBusy = true;
        StatusText = "正在发起微软登录…";
        _loginCts = new CancellationTokenSource();

        try
        {
            var account = await _stack.MicrosoftAuth.LoginAsync(
                url =>
                {
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                    return Task.CompletedTask;
                },
                new Progress<MsAuthProgress>(p => StatusText = p.Stage),
                _loginCts.Token);

            ApplySignedIn(account);
        }
        catch (MsAuthException ex)
        {
            _stack.Log.Warn($"微软登录失败（{ex.Failure}）", ex);
            StatusText = ex.ToUserMessage();
        }
        catch (OperationCanceledException)
        {
            StatusText = "已取消登录。";
        }
        catch (Exception ex)
        {
            _stack.Log.Error("微软登录异常", ex);
            StatusText = "登录失败：" + ex.Message;
        }
        finally
        {
            _loginCts?.Dispose();
            _loginCts = null;
            IsBusy = false;
        }
    }

    /// <summary>
    /// 设备码登录：不碰 redirect_uri，给用户一个短码让他到 microsoft.com/devicelogin 输入。
    /// 这是绕开 Azure 回调地址配置问题的可靠办法。
    /// </summary>
    private async Task LoginByDeviceCodeAsync()
    {
        IsBusy = true;
        StatusText = "正在申请设备码…";
        _loginCts = new CancellationTokenSource();

        try
        {
            var account = await _stack.MicrosoftAuth.LoginWithDeviceCodeAsync(
                new Progress<MsDeviceCodePrompt>(p =>
                {
                    DeviceCodeText = p.UserCode;
                    DeviceCodeUri = p.VerificationUri;
                    StatusText = $"请打开 {p.VerificationUri}，输入代码 {p.UserCode} 并授权（已自动打开浏览器）";
                    OpenDeviceCodePage();
                }),
                new Progress<MsAuthProgress>(p => StatusText = p.Stage),
                _loginCts.Token);

            ApplySignedIn(account);
        }
        catch (MsAuthException ex)
        {
            _stack.Log.Warn($"设备码登录失败（{ex.Failure}）", ex);
            StatusText = ex.ToUserMessage();
        }
        catch (OperationCanceledException)
        {
            StatusText = "已取消登录。";
        }
        catch (Exception ex)
        {
            _stack.Log.Error("设备码登录异常", ex);
            StatusText = "登录失败：" + ex.Message;
        }
        finally
        {
            DeviceCodeText = string.Empty;
            DeviceCodeUri = string.Empty;
            _loginCts?.Dispose();
            _loginCts = null;
            IsBusy = false;
        }
    }

    private void ApplySignedIn(Account account)
    {
        _stack.Accounts.Upsert(account);
        _stack.Config.Save();
        StatusText = $"已登录微软账号：{account.Name}";
        Refresh();
        _stack.RaiseChanged();
    }

    private void OpenDeviceCodePage()
    {
        var uri = _deviceCodeUri;
        if (string.IsNullOrWhiteSpace(uri))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _stack.Log.Warn($"打开设备码验证页失败：{uri}", ex);
        }
    }
}
