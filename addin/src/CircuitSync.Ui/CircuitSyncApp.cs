using System.Reflection;
using System.Windows.Media.Imaging;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
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

        // Satu-satunya cara mengetahui model berubah tanpa memindai ulang terus-menerus.
        // Handler-nya hanya menandai; pekerjaannya menunggu detak timer di controller.
        application.ControlledApplication.DocumentChanged += OnDocumentChanged;

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
        application.ControlledApplication.DocumentChanged -= OnDocumentChanged;
        _window?.Close();
        _controller?.Dispose();
        return Result.Succeeded;
    }

    /// <summary>
    /// Menandai model sebagai berubah supaya tarikan berikutnya terkirim sendiri.
    /// </summary>
    /// <remarks>
    /// Berjalan pada setiap transaksi Revit, termasuk milik add-in lain, jadi harus
    /// murah dan tidak boleh melempar: exception dari sini muncul sebagai dialog error
    /// Revit di tengah pekerjaan user.
    /// </remarks>
    private static void OnDocumentChanged(object? sender, DocumentChangedEventArgs args)
    {
        if (_controller is null)
        {
            return;
        }

        try
        {
            if (TouchesElectrical(args))
            {
                _controller.NoteModelChanged();
            }
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException)
        {
        }
    }

    /// <summary>
    /// Benar kalau perubahan menyentuh device atau panel. Memindahkan dinding tidak perlu
    /// menghasilkan tarikan model seukuran gudang.
    /// </summary>
    private static bool TouchesElectrical(DocumentChangedEventArgs args)
    {
        // Elemen yang dihapus tidak bisa diperiksa kategorinya — sudah tidak ada di
        // dokumen. Penghapusan apa pun dianggap relevan.
        if (args.GetDeletedElementIds().Count > 0)
        {
            return true;
        }

        var added = args.GetAddedElementIds();
        var modified = args.GetModifiedElementIds();

        // Perubahan sebesar ini biasanya memuat elektrikal juga, dan memeriksanya satu per
        // satu di thread utama Revit lebih mahal daripada tarikan model yang mungkin sia-sia.
        if (added.Count + modified.Count > 500)
        {
            return true;
        }

        var doc = args.GetDocument();
        foreach (var id in added.Concat(modified))
        {
            if (doc.GetElement(id)?.Category?.BuiltInCategory is
                BuiltInCategory.OST_LightingFixtures or
                BuiltInCategory.OST_ElectricalFixtures or
                BuiltInCategory.OST_ElectricalEquipment)
            {
                return true;
            }
        }

        return false;
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
