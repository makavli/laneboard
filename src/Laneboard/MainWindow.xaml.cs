using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Laneboard;

public partial class MainWindow : Window
{
    readonly Settings _s;
    readonly bool _demo;
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(60) };

    List<LocalInfo> _locals = [];
    Dictionary<string, RemoteInfo> _remote = new(StringComparer.OrdinalIgnoreCase);
    AccountInfo? _account;
    List<ProjectView> _views = [];
    DateTime _lastLocal = DateTime.MinValue, _lastRemote = DateTime.MinValue, _hiddenAt = DateTime.MinValue;
    bool _busy, _allowClose, _dialogOpen, _placed, _syncing;

    static readonly TimeSpan RemoteEvery = TimeSpan.FromMinutes(5);

    public event Action<string>? SummaryChanged;

    public MainWindow(Settings settings, bool demo)
    {
        _s = settings;
        _demo = demo;
        InitializeComponent();
        AboutText.Text = $"{App.Name} {typeof(App).Assembly.GetName().Version?.ToString(3)} · free and open source";
        ApplyPinned();
        ApplyAppearance();
        _saveSoon.Tick += (_, _) => { _saveSoon.Stop(); Save(); };

        SourceInitialized += (_, _) => Backdrop.Apply(this, _s.Pinned, _s.Theme);
        SystemEvents.UserPreferenceChanged += (_, _) => Dispatcher.Invoke(() => Backdrop.Apply(this, _s.Pinned, _s.Theme));
        Deactivated += (_, _) =>
        {
            if (_s.Pinned || _dialogOpen || !IsVisible) return;
            Hide();
            _hiddenAt = DateTime.UtcNow;
        };
        Closing += (_, e) =>
        {
            if (_allowClose) return;
            e.Cancel = true;
            Hide();
        };
        PreviewKeyDown += OnKey;
        _timer.Tick += async (_, _) => await Tick();
        _timer.Start();
    }

    public void AllowClose()
    {
        _allowClose = true;
        Close();
    }

    // ---- Showing and hiding: a flyout above the notification area, or a normal window once pinned. ----

    public void ShowFlyout()
    {
        if (!_s.Pinned || !_placed) PlaceNearTray();
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        if (DateTime.UtcNow - _lastLocal > TimeSpan.FromSeconds(20)) _ = RefreshAsync(false);
    }

    public void ToggleFlyout()
    {
        if (IsVisible && IsActive) { Hide(); return; }
        // A click on the tray icon first deactivates (and hides) the flyout: that click must not reopen it.
        if (DateTime.UtcNow - _hiddenAt < TimeSpan.FromMilliseconds(400)) return;
        ShowFlyout();
    }

    void PlaceNearTray()
    {
        // shortcut: primary screen's work area only; on a multi-monitor setup the tray's own screen would be better.
        var wa = SystemParameters.WorkArea;
        Height = Math.Min(720, wa.Height - 24);
        Left = wa.Right - Width - 12;
        Top = wa.Bottom - Height - 12;
        _placed = true;
    }

    void ApplyPinned()
    {
        PinBtn.Content = _s.Pinned ? "" : "";
        PinBtn.ToolTip = _s.Pinned ? "Close when I click elsewhere" : "Keep open";
        ShowInTaskbar = _s.Pinned;
        _syncing = true;
        PinBox.IsChecked = _s.Pinned;
        _syncing = false;
        if (IsLoaded) Backdrop.Apply(this, _s.Pinned, _s.Theme);
    }

    void SetPinned(bool pinned)
    {
        if (_s.Pinned == pinned) return;
        _s.Pinned = pinned;
        Save();
        ApplyPinned();
    }

    // ---- Data ----

    async Task Tick()
    {
        UpdateAgo();
        bool due = IsVisible ? DateTime.UtcNow - _lastLocal >= TimeSpan.FromSeconds(55) : DateTime.UtcNow - _lastLocal >= RemoteEvery;
        if (due) await RefreshAsync(false);
    }

    public async Task RefreshAsync(bool full)
    {
        if (_busy) return;
        _busy = true;
        RefreshBtn.IsEnabled = false;
        try
        {
            if (_demo)
            {
                (_locals, _remote, _account) = Demo.Data(DateTimeOffset.Now);
                _lastRemote = DateTime.UtcNow;
            }
            else
            {
                var paths = await Task.Run(() => Scanner.Discover(_s.Roots, _s.Hidden));
                _locals = await Scanner.ReadAll(paths);
                if (full || DateTime.UtcNow - _lastRemote > RemoteEvery)
                {
                    var slugs = _locals.Select(l => l.Slug).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                    var account = GitHub.Account();
                    var repos = GitHub.Repos(slugs);
                    await Task.WhenAll(account, repos);
                    _account = account.Result;
                    _remote = repos.Result;
                    _lastRemote = DateTime.UtcNow;
                }
            }
            _lastLocal = DateTime.UtcNow;
            Render();
        }
        finally
        {
            _busy = false;
            RefreshBtn.IsEnabled = true;
        }
    }

    void Render()
    {
        var board = Board.Build(_locals, _remote, _account, !_demo && Scanner.LocalRunner(), DateTimeOffset.Now, Shell.HasCode);
        _views = board.Projects.ToList();

        SummaryText.Text = board.Summary;
        RunnerChip.Visibility = board.RunnerChip == null ? Visibility.Collapsed : Visibility.Visible;
        RunnerText.Text = board.RunnerChip ?? "";
        MinutesChip.Visibility = board.MinutesChip == null ? Visibility.Collapsed : Visibility.Visible;
        MinutesText.Text = board.MinutesChip ?? "";
        ScopeChip.Visibility = board.NeedsUserScope ? Visibility.Visible : Visibility.Collapsed;
        ScopePanel.Visibility = ScopeChip.Visibility;
        GlobalWarnings.ItemsSource = board.Warnings;

        GitHubStatus.Text = _account switch
        {
            null => "Not checked yet.",
            { GhFound: false } => "GitHub CLI not found. Install it from cli.github.com to see pull requests and checks.",
            { SignedIn: false } => "GitHub CLI is installed but not signed in. Run gh auth login in a terminal.",
            _ => $"Signed in to GitHub as {_account.Login}.",
        };

        bool empty = _s.Roots.Count == 0 && !_demo;
        Empty.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        TopPanel.Visibility = empty || SettingsPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        ApplyFilter();
        UpdateAgo();
        SummaryChanged?.Invoke(board.TrayText);
    }

    void ApplyFilter()
    {
        var q = Search.Text.Trim();
        var shown = q.Length == 0 ? _views : _views.Where(v =>
            v.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || v.Branch.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        Cards.ItemsSource = shown;
        NoMatch.Visibility = shown.Count == 0 && _views.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        SearchHint.Visibility = Search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    void UpdateAgo() =>
        UpdatedText.Text = _lastLocal == DateTime.MinValue ? "Reading your projects…" : "Updated " + Parse.Ago(DateTime.UtcNow - _lastLocal);

    void Save()
    {
        if (!_demo) _s.Save();
    }

    // ---- Settings page ----

    public void ShowSettings(bool on)
    {
        SettingsPanel.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        ListScroll.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        BackBtn.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        SettingsBtn.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        if (on)
        {
            Empty.Visibility = Visibility.Collapsed;
            FolderList.ItemsSource = _s.Roots.ToList();
            HiddenList.ItemsSource = _s.Hidden.ToList();
            HiddenCard.Visibility = _s.Hidden.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            _syncing = true;
            StartupBox.IsChecked = !_demo && Settings.StartWithWindows;
            ThemeSystem.IsChecked = _s.Theme == "System";
            ThemeLight.IsChecked = _s.Theme == "Light";
            ThemeDark.IsChecked = _s.Theme == "Dark";
            ViewLight.IsChecked = _s.View != "Advanced";
            ViewAdvanced.IsChecked = _s.View == "Advanced";
            OpacitySlider.Value = _s.Solidity;
            _syncing = false;
        }
        ViewBtn.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        Render();
    }

    void OnSettings(object sender, RoutedEventArgs e) => ShowSettings(true);
    void OnBack(object sender, RoutedEventArgs e) => ShowSettings(false);

    async void OnAddFolder(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Choose a folder that holds your projects", Multiselect = true };
        _dialogOpen = true;
        bool ok;
        try { ok = dlg.ShowDialog(this) == true; }
        finally { _dialogOpen = false; }
        if (!ok) return;
        foreach (var f in dlg.FolderNames)
            if (!_s.Roots.Contains(f, StringComparer.OrdinalIgnoreCase)) _s.Roots.Add(f);
        Save();
        if (SettingsPanel.IsVisible) FolderList.ItemsSource = _s.Roots.ToList();
        await RefreshAsync(true);
    }

    async void OnRemoveFolder(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string f }) return;
        _s.Roots.RemoveAll(r => r.Equals(f, StringComparison.OrdinalIgnoreCase));
        Save();
        FolderList.ItemsSource = _s.Roots.ToList();
        await RefreshAsync(false);
    }

    async void OnUnhide(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string p }) return;
        _s.Hidden.RemoveAll(h => h.Equals(p, StringComparison.OrdinalIgnoreCase));
        Save();
        HiddenList.ItemsSource = _s.Hidden.ToList();
        HiddenCard.Visibility = _s.Hidden.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        await RefreshAsync(false);
    }

    void OnStartup(object sender, RoutedEventArgs e)
    {
        if (_syncing || _demo) return;
        Settings.StartWithWindows = StartupBox.IsChecked == true;
    }

    void OnPinBox(object sender, RoutedEventArgs e)
    {
        if (!_syncing) SetPinned(PinBox.IsChecked == true);
    }

    void OnCopyScope(object sender, RoutedEventArgs e) => Clipboard.SetText(ScopeCommand.Text);

    // ---- Appearance: theme, view, glass-to-solid background ----

    void ApplyAppearance()
    {
        var mode = _s.Theme switch { "Light" => ThemeMode.Light, "Dark" => ThemeMode.Dark, _ => ThemeMode.System };
        if (Application.Current.ThemeMode != mode) Application.Current.ThemeMode = mode;
        if (IsLoaded) Backdrop.Apply(this, _s.Pinned, _s.Theme);

        bool advanced = _s.View == "Advanced";
        Cards.ItemTemplate = (DataTemplate)FindResource(advanced ? "CardTemplate" : "RowTemplate");
        ViewBtn.Content = advanced ? "" : "";
        ViewBtn.ToolTip = advanced ? "Light view" : "Advanced view";

        Tint.Opacity = Math.Clamp(_s.Solidity, 0, 100) / 100.0;
    }

    void OnToggleView(object sender, RoutedEventArgs e)
    {
        _s.View = _s.View == "Advanced" ? "Light" : "Advanced";
        Save();
        ApplyAppearance();
    }

    void OnTheme(object sender, RoutedEventArgs e)
    {
        if (_syncing || sender is not RadioButton { Content: string label }) return;
        _s.Theme = label;
        Save();
        ApplyAppearance();
    }

    void OnView(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        _s.View = ViewAdvanced.IsChecked == true ? "Advanced" : "Light";
        Save();
        ApplyAppearance();
    }

    readonly DispatcherTimer _saveSoon = new() { Interval = TimeSpan.FromMilliseconds(400) };

    void OnOpacity(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncing || Tint == null) return;
        _s.Solidity = (int)Math.Round(e.NewValue);
        Tint.Opacity = _s.Solidity / 100.0;
        // Dragging the slider fires many times: write the settings file once it rests.
        _saveSoon.Stop();
        _saveSoon.Start();
    }

    // ---- Title bar and cards ----

    async void OnRefresh(object sender, RoutedEventArgs e) => await RefreshAsync(true);
    void OnPin(object sender, RoutedEventArgs e) => SetPinned(!_s.Pinned);
    void OnClose(object sender, RoutedEventArgs e) => Hide();
    void OnSearch(object sender, TextChangedEventArgs e) => ApplyFilter();

    void OnOpenUrl(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string url }) Shell.OpenUrl(url);
    }

    async void OnAction(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ProjectView p, Tag: string tag }) return;
        switch (tag)
        {
            case "folder": Shell.Open(p.Path); break;
            case "code": Shell.Code(p.Path); break;
            case "terminal": Shell.Terminal(p.Path); break;
            case "github": if (p.Slug != null) Shell.OpenUrl($"https://github.com/{p.Slug}"); break;
            case "handoff": if (p.HandoffPath != null) Shell.Open(p.HandoffPath); break;
            case "copy": Clipboard.SetText(p.Path); break;
            case "hide":
                _s.Hidden.Add(p.Path);
                Save();
                await RefreshAsync(false);
                break;
        }
    }

    async void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (SettingsPanel.IsVisible) ShowSettings(false);
            else if (Search.Text.Length > 0) Search.Text = "";
            else Hide();
            e.Handled = true;
        }
        else if (e.Key == Key.F5) { e.Handled = true; await RefreshAsync(true); }
        else if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control) { Search.Focus(); e.Handled = true; }
    }
}
