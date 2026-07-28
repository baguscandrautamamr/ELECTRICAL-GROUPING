using System.Reflection;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;
using CircuitSync.Core;
using CircuitSync.Revit;

namespace CircuitSync.Ui;

/// <summary>
/// Titik masuk add-in: membangun ribbon dan menyiapkan antrean external event.
/// State yang hidup selama sesi Revit disimpan di sini karena command Revit dibuat
/// ulang setiap kali diklik.
/// </summary>
public sealed class CircuitSyncApp : IExternalApplication
{
    private static RevitTaskQueue? _queue;
    private static SyncController? _controller;
    private static MainWindow? _window;

    public static AddinSettings Settings { get; private set; } = new();

    public static Translator Translator { get; private set; } = new();

    public Result OnStartup(UIControlledApplication application)
    {
        Settings = AddinSettings.Load();
        Translator = new Translator(Settings.Language);

        _queue = new RevitTaskQueue();
        _queue.Initialize();

        var tab = Translator["revit.tab_name"];
        try
        {
            application.CreateRibbonTab(tab);
        }
        catch (Autodesk.Revit.Exceptions.ArgumentException)
        {
            // Tab sudah dibuat add-in lain. Itu bukan masalah — panel kita tetap masuk.
        }

        var panel = application.CreateRibbonPanel(tab, Translator["revit.panel_name"]);
        var assemblyPath = Assembly.GetExecutingAssembly().Location;

        var button = new PushButtonData(
            "CircuitSyncOpenPanel",
            Translator["revit.button_text"],
            assemblyPath,
            typeof(OpenPanelCommand).FullName);

        if (panel.AddItem(button) is PushButton pushButton)
        {
            pushButton.ToolTip = Translator["revit.command_tooltip"];
            pushButton.LargeImage = LoadIcon("ribbon-32.png");
            pushButton.Image = LoadIcon("ribbon-16.png");
        }

        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        _window?.Close();
        _controller?.Dispose();
        return Result.Succeeded;
    }

    /// <summary>
    /// Menampilkan panel, membuatnya kalau belum ada. Window non-modal, jadi hanya satu
    /// instance yang boleh hidup — klik kedua cukup memunculkannya ke depan.
    /// </summary>
    internal static void ShowPanel(UIApplication application)
    {
        _queue ??= new RevitTaskQueue();
        _queue.Initialize();

        if (_window is { IsLoaded: true })
        {
            _window.Activate();
            return;
        }

        _controller ??= new SyncController(_queue, Settings);

        var window = new MainWindow(_controller, Settings, Translator, application.Application.VersionNumber);
        _window = window;
        window.Closed += (_, _) => _window = null;

        // Menempelkan window ke jendela utama Revit supaya tidak hilang di belakangnya.
        new System.Windows.Interop.WindowInteropHelper(window)
        {
            Owner = application.MainWindowHandle,
        };

        window.Show();

        _ = _controller.InitializeAsync();
        _controller.SetAutoPoll(Settings.AutoPoll, Settings.PollSeconds);
    }

    /// <summary>
    /// Ikon dibaca dari embedded resource, bukan dari file di samping DLL: satu file
    /// yang hilang tidak boleh membuat ribbon gagal dibangun.
    /// </summary>
    private static BitmapImage? LoadIcon(string fileName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var name = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(fileName, StringComparison.OrdinalIgnoreCase));

        if (name is null)
        {
            return null;
        }

        using var stream = assembly.GetManifestResourceStream(name);
        if (stream is null)
        {
            return null;
        }

        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
