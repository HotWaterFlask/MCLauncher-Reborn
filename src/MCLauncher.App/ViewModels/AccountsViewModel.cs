using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
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
    private readonly AsyncRelayCommand _loginCommand;

    private string _newAccountName = string.Empty;
    private string _statusText = "离线账号无需密码，UUID 由玩家名按协议派生。";
    private bool _isBusy;
    private CancellationTokenSource? _loginCts;
    private string? _deviceCodeText;
    private string? _deviceCodeLink;

    public AccountsViewModel(LauncherStack stack)
    {
        _stack = stack;

        AddOfflineCommand = new RelayCommand(AddOffline);
        RemoveCommand = new RelayCommand<AccountItem>(Remove);
        SelectCommand = new RelayCommand<AccountItem>(Select);
        RefreshCommand = new RelayCommand(Refresh);
        CancelLoginCommand = new RelayCommand(() => _loginCts?.Cancel());
        OpenDeviceCodePageCommand = new RelayCommand(OpenDeviceCodePage);
        CopyDeviceCodeCommand = new RelayCommand(CopyDeviceCode);

        _loginCommand = new AsyncRelayCommand(LoginMicrosoftAsync, () => !IsBusy && HasMsClientId);
        LoginMicrosoftCommand = _loginCommand;

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
                _loginCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>设备码登录时展示给用户的短码；未在登录流程中时为空。</summary>
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

    /// <summary>浏览器打开的微软验证页地址。</summary>
    public string DeviceCodeLink
    {
        get => _deviceCodeLink ?? string.Empty;
        private set
        {
            _deviceCodeLink = string.IsNullOrWhiteSpace(value) ? null : value;
            OnPropertyChanged();
        }
    }

    public bool HasDeviceCode => _deviceCodeText is not null;

    /// <summary>是否已配置微软客户端 ID（未配置时界面给出引导，而不是报错）。</summary>
    public bool HasMsClientId => _stack.Config.ResolveMsClientId() is not null;

    public string MsClientIdHint => HasMsClientId
        ? "已配置客户端 ID，可进行微软正版登录。"
        : "尚未配置微软客户端 ID：请到「设置」页填入 Azure 应用客户端 ID 后再登录。";

    public string MsSetupHint => _stack.MicrosoftAuth.DescribeSetup();

    public string SelectedAccountText => _stack.Accounts.Selected is { } account
        ? $"当前账号：{account}"
        : "当前没有可用账号（启动游戏时会自动创建一个离线账号）";

    public ICommand AddOfflineCommand { get; }

    public ICommand RemoveCommand { get; }

    public ICommand SelectCommand { get; }

    public ICommand RefreshCommand { get; }

    public ICommand LoginMicrosoftCommand { get; }

    public ICommand OpenDeviceCodePageCommand { get; }

    /// <summary>复制短码（浏览器没自动打开时，用户可以手贴或用它手动输入）。</summary>
    public ICommand CopyDeviceCodeCommand { get; }

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
        OnPropertyChanged(nameof(MsSetupHint));
        OnPropertyChanged(nameof(SelectedAccountText));
        _loginCommand.RaiseCanExecuteChanged();
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

    /// <summary>
    /// 微软正版登录：申请设备码后打开微软验证页（<c>www.microsoft.com/link</c>），
    /// 验证码可一键复制、粘贴到页面里确认授权。
    /// </summary>
    private async Task LoginMicrosoftAsync()
    {
        IsBusy = true;
        StatusText = "正在申请登录码…";
        _loginCts = new CancellationTokenSource();

        try
        {
            var account = await _stack.MicrosoftAuth.LoginWithDeviceCodeAsync(
                new Progress<MsDeviceCodePrompt>(p =>
                {
                    DeviceCodeText = p.UserCode;
                    DeviceCodeLink = p.LinkUri;
                    StatusText = "浏览器已打开登录页，请粘贴验证码并确认授权";
                    OpenDeviceCodePage();
                }),
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
            DeviceCodeText = string.Empty;
            DeviceCodeLink = string.Empty;
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

    private void CopyDeviceCode()
    {
        if (string.IsNullOrWhiteSpace(_deviceCodeText))
        {
            return;
        }

        try
        {
            Clipboard.SetText(_deviceCodeText!);
            StatusText = "验证码已复制到剪贴板。";
        }
        catch (Exception ex)
        {
            _stack.Log.Warn("复制验证码失败", ex);
            StatusText = "复制失败：" + ex.Message;
        }
    }

    private void OpenDeviceCodePage()
    {
        var uri = _deviceCodeLink;
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
            _stack.Log.Warn($"打开验证页失败：{uri}", ex);
        }
    }
}
