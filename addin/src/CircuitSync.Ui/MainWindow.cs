using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CircuitSync.Core;
using CoreLanguage = CircuitSync.Core.Language;

namespace CircuitSync.Ui;

/// <summary>
/// Panel CircuitSync. Non-modal: user tetap bisa bekerja di Revit sambil window terbuka,
/// dan karena itu tidak satu pun handler di sini memanggil Revit API langsung —
/// semuanya lewat <see cref="SyncController"/>.
///
/// Seluruh isi window dibangun sekali; perubahan state hanya menyembunyikan atau
/// menampilkan bagian. Membangun ulang pohon visual akan menumpuk callback tema.
/// </summary>
public sealed class MainWindow : Window
{
    private readonly SyncController _controller;
    private readonly AddinSettings _settings;
    private readonly Translator _translator;
    private readonly UiKit _ui;
    private readonly List<LogEntry> _log = [];

    private readonly StackPanel _signedOut;
    private readonly StackPanel _signedIn;
    private readonly StackPanel _codeRow;
    private readonly StackPanel _passwordRow;
    private readonly StackPanel _projectBound;
    private readonly StackPanel _projectUnbound;
    private readonly StackPanel _logHost;

    private readonly TextBox _emailField;
    private readonly PasswordBox _passwordField;
    private readonly TextBox _codeField;
    private readonly TextBox _newProjectField;
    private readonly ComboBox _projectPicker;
    private readonly ComboBox _intervalPicker;
    private readonly CheckBox _autoToggle;
    private readonly CheckBox _tagToggle;

    private readonly TextBlock _accountValue;
    private readonly TextBlock _projectValue;
    private readonly TextBlock _summaryValue;
    private readonly TextBlock _busyDot;
    private readonly Button _languageButton;
    private readonly Button _themeButton;

    public MainWindow(SyncController controller, AddinSettings settings, Translator translator, string revitVersion)
    {
        _controller = controller;
        _settings = settings;
        _translator = translator;
        _ui = new UiKit(translator);

        Title = "CircuitSync";
        Width = 460;
        Height = 780;
        MinWidth = 380;
        MinHeight = 520;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        // ------------------------------------------------------------- header
        _languageButton = _ui.Quiet("", (_, _) => ToggleLanguage());
        _themeButton = _ui.Quiet("", (_, _) => ToggleTheme());
        _busyDot = _ui.Value();
        _busyDot.VerticalAlignment = VerticalAlignment.Center;

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titles = UiKit.Stack(2,
            _ui.Title("app.title"),
            _ui.Muted("app.subtitle", revitVersion));
        var toggles = UiKit.Row(6, _busyDot, _languageButton, _themeButton);
        toggles.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(toggles, 1);
        header.Children.Add(titles);
        header.Children.Add(toggles);

        // ------------------------------------------------------------- auth card
        _emailField = _ui.Field();
        _passwordField = _ui.Secret();
        _codeField = _ui.Field();

        _passwordRow = UiKit.Stack(10,
            _ui.Labeled("auth.password", _passwordField),
            UiKit.Row(8,
                _ui.Primary("auth.sign_in", (_, _) => Run(_controller.SignInWithPasswordAsync(
                    _emailField.Text, _passwordField.Password))),
                _ui.QuietKey("auth.use_code", (_, _) => SetCodeMode(true))));

        _codeRow = UiKit.Stack(10,
            _ui.Labeled("auth.code", _codeField),
            UiKit.Row(8,
                _ui.Secondary("auth.send_code", (_, _) => RequireEmail(email =>
                    Run(_controller.SendEmailCodeAsync(email)))),
                _ui.Primary("auth.verify", (_, _) => RequireEmail(email =>
                    Run(_controller.VerifyEmailCodeAsync(email, _codeField.Text)))),
                _ui.QuietKey("auth.use_password", (_, _) => SetCodeMode(false))));

        _signedOut = UiKit.Stack(10,
            _ui.Labeled("auth.email", _emailField),
            _passwordRow,
            _codeRow);

        _accountValue = _ui.Value();
        _signedIn = UiKit.Stack(10,
            _accountValue,
            _ui.Secondary("auth.sign_out", (_, _) => Run(_controller.SignOutAsync())));

        var authCard = _ui.Card(UiKit.Stack(12, _ui.Heading("auth.heading"), _signedOut, _signedIn));

        // ------------------------------------------------------------- project card
        _projectValue = _ui.Value();
        _projectBound = UiKit.Stack(10,
            _projectValue,
            _ui.Secondary("project.unlink", (_, _) => _controller.UnlinkProject()));

        _newProjectField = _ui.Field();
        _projectPicker = _ui.Picker();
        _projectUnbound = UiKit.Stack(12,
            _ui.Muted("project.none"),
            _ui.Labeled("project.name", _newProjectField),
            _ui.Primary("project.create", (_, _) => CreateProject()),
            _ui.Labeled("project.link", _projectPicker),
            UiKit.Row(8,
                _ui.Secondary("project.link", (_, _) => LinkProject()),
                _ui.QuietKey("project.reload", (_, _) => Run(_controller.ReloadProjectsAsync()))));

        var projectCard = _ui.Card(UiKit.Stack(12, _ui.Heading("project.heading"), _projectBound, _projectUnbound));

        // ------------------------------------------------------------- sync card
        _summaryValue = _ui.Value();
        _autoToggle = _ui.Toggle("action.auto", settings.PollSeconds);
        _autoToggle.IsChecked = settings.AutoPoll;
        _autoToggle.Checked += (_, _) => ApplyPolling();
        _autoToggle.Unchecked += (_, _) => ApplyPolling();

        _tagToggle = _ui.Toggle("action.place_tags");
        _tagToggle.IsChecked = settings.PlaceTags;
        _tagToggle.Checked += (_, _) => SaveTagToggle();
        _tagToggle.Unchecked += (_, _) => SaveTagToggle();

        _intervalPicker = _ui.Picker();
        foreach (var seconds in new[] { 10, 20, 60, 180 })
        {
            _intervalPicker.Items.Add(seconds);
        }

        _intervalPicker.SelectedItem = new[] { 10, 20, 60, 180 }.Contains(settings.PollSeconds)
            ? settings.PollSeconds
            : 20;
        _intervalPicker.SelectionChanged += (_, _) => ApplyPolling();

        var syncCard = _ui.Card(UiKit.Stack(12,
            _ui.Heading("action.heading"),
            _summaryValue,
            UiKit.Row(8,
                _ui.Primary("action.push", (_, _) => _controller.PushSnapshot()),
                _ui.Secondary("action.check", (_, _) => _controller.CheckJobs())),
            _autoToggle,
            _intervalPicker,
            _tagToggle,
            _ui.QuietKey("action.open_web", (_, _) => OpenWeb())));

        // ------------------------------------------------------------- log card
        _logHost = new StackPanel { Orientation = Orientation.Vertical };
        var logCard = _ui.Card(UiKit.Stack(10, _ui.Heading("log.heading"), _logHost));

        var root = UiKit.Stack(16, header, authCard, projectCard, syncCard, logCard);
        root.Margin = new Thickness(20);

        Content = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = root,
        };

        _controller.StateChanged += OnStateChanged;
        _controller.Logged += OnLogged;
        Closed += (_, _) =>
        {
            _controller.StateChanged -= OnStateChanged;
            _controller.Logged -= OnLogged;
        };

        SetCodeMode(false);
        ApplyTheme();
        ApplyLanguage();
        UpdateState();
    }

    // ---------------------------------------------------------------- theme & language

    private void ToggleTheme()
    {
        _settings.Theme = _settings.Theme == ThemeMode.Light ? ThemeMode.Dark : ThemeMode.Light;
        _settings.Save();
        ApplyTheme();
    }

    private void ApplyTheme()
    {
        var palette = Palette.For(_settings.Theme);
        _ui.Restyle(palette);
        Background = new SolidColorBrush(palette.Background);

        // Tombol tema menyebut mode yang akan dituju, bukan yang sedang aktif.
        _themeButton.Content = _translator[_settings.Theme == ThemeMode.Light ? "theme.dark" : "theme.light"];
        RenderLog();
    }

    private void ToggleLanguage()
    {
        _settings.Language = _settings.Language == CoreLanguage.Id ? CoreLanguage.En : CoreLanguage.Id;
        _settings.Save();
        ApplyLanguage();
    }

    private void ApplyLanguage()
    {
        _translator.Language = _settings.Language;
        _ui.Retext();

        _languageButton.Content = _settings.Language == CoreLanguage.Id ? "ID" : "EN";
        _themeButton.Content = _translator[_settings.Theme == ThemeMode.Light ? "theme.dark" : "theme.light"];
        ApplyPollingLabel();
        UpdateState();
        RenderLog();
    }

    /// <summary>
    /// Dua cara masuk: kata sandi, atau kode yang dikirim ke email. Hanya satu baris
    /// yang terlihat supaya tidak ada dua tombol "masuk" di layar sekaligus.
    /// </summary>
    private void SetCodeMode(bool useCode)
    {
        _passwordRow.Visibility = useCode ? Visibility.Collapsed : Visibility.Visible;
        _codeRow.Visibility = useCode ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---------------------------------------------------------------- state

    private void OnStateChanged() => Dispatcher.Invoke(UpdateState);

    private void OnLogged(LogEntry entry) => Dispatcher.Invoke(() =>
    {
        _log.Add(entry);
        if (_log.Count > 100)
        {
            _log.RemoveRange(0, _log.Count - 100);
        }

        RenderLog();
    });

    private void UpdateState()
    {
        var signedIn = _controller.IsSignedIn;
        _signedIn.Visibility = signedIn ? Visibility.Visible : Visibility.Collapsed;
        _signedOut.Visibility = signedIn ? Visibility.Collapsed : Visibility.Visible;
        _accountValue.Text = _translator.Get("auth.signed_in_as", _controller.UserEmail ?? "");

        var bound = _controller.ProjectId is not null;
        _projectBound.Visibility = bound ? Visibility.Visible : Visibility.Collapsed;
        _projectUnbound.Visibility = bound || !signedIn ? Visibility.Collapsed : Visibility.Visible;
        _projectValue.Text = _translator.Get("project.bound", _controller.ProjectName ?? "—");

        SyncProjectPicker();

        var snapshot = _controller.LastSnapshot;
        _summaryValue.Text = snapshot is null
            ? _translator["log.empty"]
            : string.Join("  ·  ",
                _translator.Get("summary.devices", snapshot.Devices.Count),
                _translator.Get("summary.panels", snapshot.Panels.Count),
                _translator.Get("summary.usable_panels", snapshot.Panels.Count(p => p.IsUsable)));

        _busyDot.Text = _controller.Busy ? "···" : "";
    }

    private void SyncProjectPicker()
    {
        var names = _controller.Projects.Select(p => p.Name).ToList();
        if (_projectPicker.Items.Count == names.Count &&
            _projectPicker.Items.Cast<object>().Select(i => i as string).SequenceEqual(names))
        {
            return;
        }

        var selected = _projectPicker.SelectedIndex;
        _projectPicker.Items.Clear();
        foreach (var name in names)
        {
            _projectPicker.Items.Add(name);
        }

        _projectPicker.SelectedIndex = selected >= 0 && selected < names.Count ? selected : (names.Count > 0 ? 0 : -1);
    }

    private void RenderLog()
    {
        _logHost.Children.Clear();

        if (_log.Count == 0)
        {
            _logHost.Children.Add(new TextBlock
            {
                Text = _translator["log.empty"],
                FontFamily = UiKit.Face,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(_ui.Palette.TextMuted),
            });
            return;
        }

        foreach (var entry in Enumerable.Reverse(_log).Take(30))
        {
            _logHost.Children.Add(new TextBlock
            {
                Text = $"{entry.At:HH:mm}  {_translator.Get(entry.Key, entry.Args)}",
                FontFamily = UiKit.Face,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 4),
                Foreground = new SolidColorBrush(ColorFor(entry.Kind)),
            });
        }
    }

    private Color ColorFor(LogKind kind) => kind switch
    {
        LogKind.Ok => _ui.Palette.Ok,
        LogKind.Warn => _ui.Palette.Warn,
        LogKind.Error => _ui.Palette.Danger,
        _ => _ui.Palette.TextMuted,
    };

    // ---------------------------------------------------------------- actions

    private void CreateProject()
    {
        var name = _newProjectField.Text.Trim();
        if (name.Length == 0)
        {
            OnLogged(new LogEntry(DateTime.Now, LogKind.Warn, "project.need_name", []));
            return;
        }

        Run(_controller.CreateProjectAsync(name));
        _newProjectField.Text = "";
    }

    private void LinkProject()
    {
        var index = _projectPicker.SelectedIndex;
        if (index < 0 || index >= _controller.Projects.Count)
        {
            OnLogged(new LogEntry(DateTime.Now, LogKind.Warn, "project.need_selection", []));
            return;
        }

        _controller.LinkProject(_controller.Projects[index].Id);
    }

    private void RequireEmail(Action<string> withEmail)
    {
        var email = _emailField.Text.Trim();
        if (email.Length == 0)
        {
            OnLogged(new LogEntry(DateTime.Now, LogKind.Warn, "auth.need_email", []));
            return;
        }

        withEmail(email);
    }

    private void ApplyPolling()
    {
        var seconds = _intervalPicker.SelectedItem is int value ? value : 20;
        _controller.SetAutoPoll(_autoToggle.IsChecked == true, seconds);
        ApplyPollingLabel();
    }

    private void ApplyPollingLabel()
    {
        var seconds = _intervalPicker.SelectedItem is int value ? value : 20;
        _autoToggle.Content = _translator.Get("action.auto", seconds);
    }

    private void SaveTagToggle()
    {
        _settings.PlaceTags = _tagToggle.IsChecked == true;
        _settings.Save();
    }

    private void OpenWeb()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = _controller.Config.WebUrl,
                UseShellExecute = true,
            });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            OnLogged(new LogEntry(DateTime.Now, LogKind.Error, "log.network", []));
        }
    }

    /// <summary>
    /// Task dari controller sengaja tidak di-await: window tidak boleh memblokir
    /// thread UI. Kegagalannya sudah dilaporkan lewat log di dalam controller.
    /// </summary>
    private static void Run(Task work) => _ = work;
}
