using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.DB.Mechanical;
using CircuitSync.Core;

namespace CircuitSync.Revit;

/// <summary>
/// Sekali tarik seluruh device, panel, dan level dari dokumen. Read-only: tidak ada
/// transaksi di sini.
/// </summary>
public static class ModelReader
{
    /// <summary>
    /// Dipakai sebagai <c>level_key</c> untuk device yang levelnya tidak bisa
    /// ditentukan — lebih baik satu keranjang yang jelas daripada device hilang
    /// dari denah tanpa penjelasan.
    /// </summary>
    public const string UnassignedLevelKey = "unassigned";

    public static ModelSnapshot Read(Document doc)
    {
        var levels = ReadLevels(doc);
        var panels = ReadPanels(doc);
        var panelIds = PanelIdsByName(panels);
        var devices = new List<DeviceRow>();
        var systems = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (row, systemId) in
                 ReadDevices(doc, BuiltInCategory.OST_LightingFixtures, DeviceKind.Lighting, levels, panelIds)
                     .Concat(ReadDevices(doc, BuiltInCategory.OST_ElectricalFixtures, DeviceKind.Receptacle, levels, panelIds)))
        {
            devices.Add(row);
            if (systemId is not null)
            {
                systems[row.RevitUniqueId] = systemId;
            }
        }

        var used = devices.Select(d => d.LevelKey).ToHashSet(StringComparer.Ordinal);
        if (used.Contains(UnassignedLevelKey) && levels.All(l => l.LevelKey != UnassignedLevelKey))
        {
            levels.Add(new LevelRow
            {
                LevelKey = UnassignedLevelKey,
                Name = "—",
                ElevationMm = 0,
                SortOrder = levels.Count,
            });
        }

        var lightingDevices = ReadLightingDevices(doc, levels);

        // Keanggotaan layout dibatasi ke baris yang benar-benar ikut terkirim. Foreign key
        // di `layout_devices` dan `layout_lighting_devices` menunjuk tabel induknya, jadi
        // keanggotaan yang menyebut elemen yang tidak ikut akan ditolak database — dan yang
        // ditolak bukan barisnya saja melainkan seluruh batch, dengan pesan yang tidak
        // menyebut elemen mana. Menyaringnya di sini membuat invariannya struktural alih-alih
        // dua filter di dua tempat yang harus dijaga tetap seiring.
        var layoutData = ReadLayouts(doc,
            devices.Select(d => d.RevitUniqueId).ToHashSet(StringComparer.Ordinal),
            lightingDevices.Select(d => d.RevitUniqueId).ToHashSet(StringComparer.Ordinal));

        return new ModelSnapshot
        {
            Levels = levels,
            Layouts = layoutData.Rows,
            Panels = panels,
            Devices = devices,
            LightingDevices = lightingDevices,
            LayoutDevices = layoutData.Devices,
            LayoutLightingDevices = layoutData.LightingDevices,
            LineStyles = ReadLineStyles(doc),
            DeviceSystems = systems,
        };
    }

    /// <summary>
    /// Saklar dan sensor: kategori <c>OST_LightingDevices</c>, bukan
    /// <c>OST_LightingFixtures</c> yang berisi lampunya.
    /// </summary>
    /// <remarks>
    /// Yang dipakai dari sini cuma letaknya. Jumlah saklar di sebuah ruangan menentukan
    /// lampu ruangan itu dipecah jadi berapa grouping — dua saklar berarti dua grouping,
    /// serapat apa pun lampunya. Tanpa data ini batas grouping hanya bisa disimpulkan
    /// dari kerapatan lampu, dan dua ruangan yang dipisah dinding tipis dilebur jadi satu.
    ///
    /// Tidak ada penyaringan family di sini: apa pun yang Revit taruh di kategori itu
    /// dianggap penentu grouping. Menebak mana yang "saklar sungguhan" dari namanya akan
    /// meleset di setiap template yang penamaannya berbeda.
    ///
    /// Yang <b>disaring</b> adalah komponen bersarang. Satu saklar bisa dimodelkan sebagai
    /// family berisi beberapa komponen di kategori yang sama — biasa pada saklar dua atau
    /// tiga gang — dan tiap komponen itu muncul sebagai <see cref="FamilyInstance"/>
    /// tersendiri di collector. Tanpa penyaringan ini satu saklar di dinding terhitung dua
    /// atau tiga, dan ruangan yang mestinya utuh terpecah sebanyak itu. Gejalanya justru
    /// tidak menunjuk ke sini: yang terlihat hanya garis wiring yang terbelah, sementara di
    /// denah Revit saklarnya cuma satu.
    ///
    /// Yang dihitung karena itu instance tingkat atas — yang engineer hitung saat melihat
    /// denah. Kalau sebuah gang memang harus jadi grouping sendiri, tempatkan instance
    /// terpisah di Revit, bukan komponen bersarang.
    /// </remarks>
    private static List<LightingDeviceRow> ReadLightingDevices(Document doc, IReadOnlyList<LevelRow> levels)
    {
        var rows = new List<LightingDeviceRow>();

        var instances = new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_LightingDevices)
            .WhereElementIsNotElementType()
            .OfClass(typeof(FamilyInstance))
            .Cast<FamilyInstance>()
            .Where(instance => instance.SuperComponent is null);

        foreach (var instance in instances)
        {
            var point = PointOf(instance);

            rows.Add(new LightingDeviceRow
            {
                RevitUniqueId = instance.UniqueId,
                FamilyKey = FamilyKey.Make(instance.Symbol.Family.Name, instance.Symbol.Name),
                LevelKey = LevelKeyOf(doc, instance, levels, point),
                RoomName = RoomNameOf(instance),
                XMm = point is null ? 0 : Units.ToMillimetersRounded(point.X),
                YMm = point is null ? 0 : Units.ToMillimetersRounded(point.Y),
            });
        }

        return rows;
    }

    /// <summary>
    /// Line style di model: subcategory kategori <c>OST_Lines</c>, yang di Revit muncul
    /// di dialog Line Styles.
    /// </summary>
    /// <remarks>
    /// Yang dikirim sebagai identitas adalah <c>UniqueId</c> GraphicsStyle-nya, bukan
    /// nama style-nya. Nama bisa diubah user, dan yang dipakai
    /// <see cref="WiringApplier"/> untuk menemukan style yang sama harus yang tidak
    /// berubah.
    ///
    /// Tidak ada penyaringan nama di sini. Menebak mana yang "style untuk wiring" dari
    /// namanya akan meleset di setiap template yang penamaannya berbeda — user yang
    /// memilih, di web, dari daftar apa adanya.
    /// </remarks>
    private static List<LineStyleRow> ReadLineStyles(Document doc)
    {
        var rows = new List<LineStyleRow>();

        // Category.GetCategory, bukan Settings.Categories.get_Item: yang kedua memanggil
        // accessor properti secara eksplisit, dan itu bergantung pada bagaimana indexer
        // Categories diterjemahkan ke C#. Yang pertama method statis biasa.
        var lines = Category.GetCategory(doc, BuiltInCategory.OST_Lines);
        if (lines is null)
        {
            return rows;
        }

        foreach (Category sub in lines.SubCategories)
        {
            // Style yang tidak punya GraphicsStyle projection tidak bisa dipasang ke
            // detail curve, jadi menawarkannya di web hanya menjanjikan kegagalan.
            if (sub.GetGraphicsStyle(GraphicsStyleType.Projection) is not { } style)
            {
                continue;
            }

            rows.Add(new LineStyleRow
            {
                RevitUniqueId = style.UniqueId,
                Name = sub.Name,
            });
        }

        return rows
            .OrderBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            .Select((row, index) => row with { SortOrder = index })
            .ToList();
    }

    /// <summary>
    /// View denah yang dipakai sebagai halaman kerja di web. Yang memutuskan view mana
    /// yang ikut adalah <see cref="LayoutFilter"/> di Core; di sini hanya penerjemahan.
    /// </summary>
    /// <param name="knownDevices">
    /// <c>UniqueId</c> device yang ikut terkirim. Keanggotaan di luar daftar ini dibuang,
    /// karena foreign key-nya akan ditolak database.
    /// </param>
    /// <param name="knownLightingDevices">Idem, untuk saklar.</param>
    private static (List<LayoutRow> Rows, List<LayoutDeviceRow> Devices,
        List<LayoutLightingDeviceRow> LightingDevices) ReadLayouts(Document doc,
        IReadOnlySet<string> knownDevices, IReadOnlySet<string> knownLightingDevices)
    {
        var rows = new List<LayoutRow>();
        var members = new List<LayoutDeviceRow>();
        var switchMembers = new List<LayoutLightingDeviceRow>();

        var views = new FilteredElementCollector(doc)
            .OfClass(typeof(ViewPlan))
            .Cast<ViewPlan>()
            .Where(view => !view.IsTemplate);

        foreach (var view in views)
        {
            var kind = LayoutFilter.KindOf(view.Name);
            if (kind is null)
            {
                continue;
            }

            // Denah tanpa level tidak bisa dipasangkan ke device mana pun, jadi tidak
            // ada gunanya dikirim sebagai halaman kerja.
            var level = view.GenLevel;
            if (level is null)
            {
                continue;
            }

            var crop = CropInMillimeters(view);

            rows.Add(new LayoutRow
            {
                RevitUniqueId = view.UniqueId,
                Name = view.Name,
                Kind = kind,
                LevelKey = level.UniqueId,
                Scale = view.Scale,
                CropMinXMm = crop?.MinX,
                CropMinYMm = crop?.MinY,
                CropMaxXMm = crop?.MaxX,
                CropMaxYMm = crop?.MaxY,
            });

            foreach (var deviceId in VisibleDeviceIds(doc, view, kind).Where(knownDevices.Contains))
            {
                members.Add(new LayoutDeviceRow
                {
                    LayoutUniqueId = view.UniqueId,
                    DeviceUniqueId = deviceId,
                });
            }

            // Saklar dibaca per view juga, dengan alasan yang sama seperti device: dua
            // denah lighting di satu lantai punya isi berbeda, dan saklar yang mengendalikan
            // lampu biasa tidak boleh ikut memecah denah emergency. Kategorinya tidak
            // bergantung `kind` — yang memutuskan tetap view-nya sendiri.
            foreach (var switchId in VisibleIds(doc, view, BuiltInCategory.OST_LightingDevices)
                         .Where(knownLightingDevices.Contains))
            {
                switchMembers.Add(new LayoutLightingDeviceRow
                {
                    LayoutUniqueId = view.UniqueId,
                    LightingDeviceUniqueId = switchId,
                });
            }
        }

        return (rows
            .OrderBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            .Select((row, index) => row with { SortOrder = index })
            .ToList(), members, switchMembers);
    }

    /// <summary>
    /// Device yang benar-benar tampak di satu view denah.
    /// </summary>
    /// <remarks>
    /// <see cref="FilteredElementCollector(Document, ElementId)"/> menyaring persis
    /// seperti yang dilihat mata di Revit: filter view, visibility per kategori, crop
    /// region, dan fase semuanya ikut berlaku. Itulah sebabnya denah lighting dan denah
    /// emergency/exit di lantai yang sama menghasilkan daftar yang berbeda, padahal
    /// pasangan (level, kind) keduanya identik.
    /// </remarks>
    private static IEnumerable<string> VisibleDeviceIds(Document doc, View view, string kind) =>
        VisibleIds(doc, view, kind == DeviceKind.Receptacle
            ? BuiltInCategory.OST_ElectricalFixtures
            : BuiltInCategory.OST_LightingFixtures);

    /// <summary>
    /// <c>UniqueId</c> family instance satu kategori yang benar-benar tampak di sebuah view.
    /// </summary>
    private static IEnumerable<string> VisibleIds(Document doc, View view, BuiltInCategory category)
    {
        try
        {
            return new FilteredElementCollector(doc, view.Id)
                .OfCategory(category)
                .WhereElementIsNotElementType()
                .OfClass(typeof(FamilyInstance))
                .Select(element => element.UniqueId)
                .ToList();
        }
        catch (Autodesk.Revit.Exceptions.ArgumentException)
        {
            // View yang belum pernah dibuka atau tidak mendukung pengumpulan per view.
            // Layout-nya tetap dikirim; isinya saja yang tidak diketahui.
            return [];
        }
    }

    /// <summary>
    /// Crop region view dalam milimeter model, atau null kalau crop tidak aktif.
    /// </summary>
    private static (double MinX, double MinY, double MaxX, double MaxY)? CropInMillimeters(View view)
    {
        if (!view.CropBoxActive)
        {
            return null;
        }

        var box = view.CropBox;
        if (box is null)
        {
            return null;
        }

        // Min dan Max hidup di koordinat kotak itu sendiri; Transform yang
        // memindahkannya ke koordinat model. Mengabaikannya membuat denah yang
        // diputar mendapat kotak yang meleset.
        var a = box.Transform.OfPoint(box.Min);
        var b = box.Transform.OfPoint(box.Max);

        return (
            Units.ToMillimetersRounded(Math.Min(a.X, b.X)),
            Units.ToMillimetersRounded(Math.Min(a.Y, b.Y)),
            Units.ToMillimetersRounded(Math.Max(a.X, b.X)),
            Units.ToMillimetersRounded(Math.Max(a.Y, b.Y)));
    }

    private static List<LevelRow> ReadLevels(Document doc)
    {
        var levels = new FilteredElementCollector(doc)
            .OfClass(typeof(Level))
            .Cast<Level>()
            .OrderBy(l => l.Elevation)
            .ToList();

        return levels.Select((level, index) => new LevelRow
        {
            LevelKey = level.UniqueId,
            Name = level.Name,
            ElevationMm = Units.ToMillimetersRounded(level.Elevation),
            SortOrder = index,
        }).ToList();
    }

    private static List<PanelRow> ReadPanels(Document doc)
    {
        var rows = new List<PanelRow>();

        var panels = new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_ElectricalEquipment)
            .WhereElementIsNotElementType()
            .OfClass(typeof(FamilyInstance))
            .Cast<FamilyInstance>();

        foreach (var panel in panels)
        {
            // Distribution system wajib terisi. Kalau None, panel tidak layak dipakai —
            // Revit sendiri akan menolak SelectPanel ke panel seperti itu.
            var distribution = Params.String(panel, BuiltInParameter.RBS_FAMILY_CONTENT_DISTRIBUTION_SYSTEM);
            var hasDistribution = !string.IsNullOrWhiteSpace(distribution) &&
                                  !distribution.Equals("None", StringComparison.OrdinalIgnoreCase);

            var name = Params.String(panel, BuiltInParameter.RBS_ELEC_PANEL_NAME) ?? panel.Name;

            rows.Add(new PanelRow
            {
                RevitUniqueId = panel.UniqueId,
                Name = name,
                Prefix = Params.TypeString(doc, panel, BuiltInParameter.RBS_ELEC_CIRCUIT_PREFIX),
                DistributionSystem = distribution,
                Voltage = Params.String(panel, BuiltInParameter.RBS_ELEC_VOLTAGE),
                Phase = Params.Int(panel, BuiltInParameter.RBS_ELEC_PANEL_NUMPHASES_PARAM),
                SlotsTotal = Params.TypeInt(doc, panel, BuiltInParameter.RBS_ELEC_MAX_POLE_BREAKERS),
                SlotsUsed = CountAssignedCircuits(panel),
                IsUsable = hasDistribution,
            });
        }

        return rows.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static int CountAssignedCircuits(FamilyInstance panel)
    {
        var systems = panel.MEPModel?.GetAssignedElectricalSystems();
        return systems?.Count ?? 0;
    }

    /// <summary>
    /// Tiap device beserta <c>UniqueId</c> ElectricalSystem yang memuatnya, atau null
    /// kalau belum masuk circuit mana pun.
    /// </summary>
    private static IEnumerable<(DeviceRow Row, string? SystemUniqueId)> ReadDevices(
        Document doc, BuiltInCategory category, string kind, IReadOnlyList<LevelRow> levels,
        IReadOnlyDictionary<string, string?> panelIds)
    {
        var instances = new FilteredElementCollector(doc)
            .OfCategory(category)
            .WhereElementIsNotElementType()
            .OfClass(typeof(FamilyInstance))
            .Cast<FamilyInstance>();

        foreach (var instance in instances)
        {
            var point = PointOf(instance);
            var (status, circuitNumber, systemUniqueId, panelName) = ReadConnection(instance);

            yield return (new DeviceRow
            {
                RevitUniqueId = instance.UniqueId,
                Kind = kind,
                LevelKey = LevelKeyOf(doc, instance, levels, point),
                RoomName = RoomNameOf(instance),
                FamilyKey = FamilyKey.Make(instance.Symbol.Family.Name, instance.Symbol.Name),
                XMm = point is null ? 0 : Units.ToMillimetersRounded(point.X),
                YMm = point is null ? 0 : Units.ToMillimetersRounded(point.Y),
                Va = ApparentLoadVa(instance),
                Status = status,
                CircuitNumber = circuitNumber,
                PanelUniqueId = PanelIdOf(panelIds, panelName),
            }, systemUniqueId);
        }
    }

    /// <summary>
    /// Titik device di koordinat model.
    /// </summary>
    /// <remarks>
    /// <see cref="LocationPoint"/> tidak selalu ada: fixture yang di-host di face atau
    /// yang berbasis garis memakai <see cref="LocationCurve"/> atau tidak punya location
    /// sama sekali. Dulu semuanya jatuh ke 0,0 — di web hasilnya setumpuk titik yang
    /// menindih satu sama lain di pojok denah. Pusat bounding box adalah jawaban yang
    /// jauh lebih dekat ke kebenaran daripada titik nol model.
    /// </remarks>
    private static XYZ? PointOf(FamilyInstance instance)
    {
        if (instance.Location is LocationPoint located)
        {
            return located.Point;
        }

        if (instance.Location is LocationCurve curve)
        {
            return curve.Curve.Evaluate(0.5, true);
        }

        // View null berarti bounding box model, bukan milik satu view tertentu.
        var box = instance.get_BoundingBox(null);
        return box is null ? null : box.Min.Add(box.Max).Multiply(0.5);
    }

    /// <summary>
    /// Empat status koneksi, ditentukan dari model — bukan dari apa yang pernah
    /// dikirim web. Snapshot berikutnya selalu menang.
    /// </summary>
    private static (string Status, string? CircuitNumber, string? SystemUniqueId, string? PanelName)
        ReadConnection(FamilyInstance instance)
    {
        if (!HasElectricalConnector(instance))
        {
            return (DeviceStatus.NoConnector, null, null, null);
        }

        var systems = instance.MEPModel?.GetElectricalSystems();
        if (systems is null || systems.Count == 0)
        {
            return (DeviceStatus.Unwired, null, null, null);
        }

        ElectricalSystem? withPanel = null;
        ElectricalSystem? withoutPanel = null;

        foreach (var system in systems)
        {
            if (string.IsNullOrWhiteSpace(system.PanelName))
            {
                withoutPanel ??= system;
            }
            else
            {
                withPanel ??= system;
            }
        }

        if (withPanel is not null)
        {
            return (DeviceStatus.Connected, Blank(withPanel.CircuitNumber), withPanel.UniqueId,
                Blank(withPanel.PanelName));
        }

        return withoutPanel is null
            ? (DeviceStatus.NoPanel, null, null, null)
            : (DeviceStatus.NoPanel, Blank(withoutPanel.CircuitNumber), withoutPanel.UniqueId, null);
    }

    /// <summary>
    /// Nama panel → <c>UniqueId</c>-nya, dari panel yang sudah dibaca dokumen ini.
    /// </summary>
    /// <remarks>
    /// <see cref="ElectricalSystem"/> hanya menyebut <b>nama</b> panel pemuatnya —
    /// <c>BaseEquipment</c> tidak ada di Revit 2025, lihat <c>docs/api-verified.md</c> —
    /// jadi nama itulah satu-satunya jembatan ke baris panel yang dikirim ke web.
    ///
    /// Nama yang muncul dua kali dipetakan ke null, bukan ke salah satunya. Revit tidak
    /// melarang dua panel bernama sama, dan menebak salah satu menghasilkan isi panel
    /// yang salah tanpa gejala apa pun di layar — lebih baik kolomnya kosong, yang di
    /// web tampak sebagai "panelnya belum diketahui".
    /// </remarks>
    private static Dictionary<string, string?> PanelIdsByName(IReadOnlyList<PanelRow> panels)
    {
        var map = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var panel in panels)
        {
            // Dipangkas di kedua sisi jembatan. Sisi pencariannya lewat Blank(), yang
            // memangkas; kalau sisi ini tidak, nama panel yang di Revit punya spasi di
            // ujung tidak akan pernah cocok — dan gejalanya cuma kolom panel yang kosong
            // tanpa alasan yang terlihat.
            var name = panel.Name.Trim();
            if (name.Length == 0)
            {
                continue;
            }

            map[name] = map.ContainsKey(name) ? null : panel.RevitUniqueId;
        }

        return map;
    }

    private static string? PanelIdOf(IReadOnlyDictionary<string, string?> panelIds, string? panelName) =>
        panelName is not null && panelIds.TryGetValue(panelName, out var id) ? id : null;

    private static bool HasElectricalConnector(FamilyInstance instance)
    {
        var manager = instance.MEPModel?.ConnectorManager;
        if (manager is null)
        {
            return false;
        }

        foreach (Connector connector in manager.Connectors)
        {
            if (connector.Domain == Domain.DomainElectrical)
            {
                return true;
            }
        }

        return false;
    }

    private static double? ApparentLoadVa(Element element)
    {
        var raw = Params.Double(element, BuiltInParameter.RBS_ELEC_APPARENT_LOAD);
        return raw is null ? null : Math.Round(Units.ToVoltAmperes(raw.Value), 1);
    }

    /// <summary>
    /// Level sebuah device, dicari bertingkat sampai ketemu.
    /// </summary>
    /// <remarks>
    /// Device yang diletakkan di lantai punya <c>LevelId</c> sendiri dan berhenti di
    /// langkah pertama. Yang di-host di dinding atau ceiling sering tidak punya, dan
    /// dulu langsung jatuh ke <c>unassigned</c> — halaman denah menyaring per level,
    /// jadi device itu terbaca dari model tapi tidak pernah muncul di web.
    /// </remarks>
    private static string LevelKeyOf(Document doc, FamilyInstance instance, IReadOnlyList<LevelRow> levels,
        XYZ? point)
    {
        if (doc.GetElement(instance.LevelId) is Level level)
        {
            return level.UniqueId;
        }

        // Fixture yang di-host di ceiling atau dinding sering tidak punya LevelId;
        // levelnya menempel di host.
        if (instance.Host is not null && doc.GetElement(instance.Host.LevelId) is Level hostLevel)
        {
            return hostLevel.UniqueId;
        }

        // Family berbasis face menyimpan levelnya di parameter Schedule Level, bukan di
        // LevelId — dan host-nya bisa berupa face yang sendirinya tidak berlevel.
        if (instance.get_Parameter(BuiltInParameter.SCHEDULE_LEVEL_PARAM)?.AsElementId() is { } scheduled &&
            doc.GetElement(scheduled) is Level scheduledLevel)
        {
            return scheduledLevel.UniqueId;
        }

        // Terakhir: simpulkan dari ketinggiannya. Menebak level yang benar jauh lebih
        // berguna daripada menyembunyikan device dari denah.
        if (point is not null &&
            LevelFinder.KeyFor(levels, Units.ToMillimetersRounded(point.Z)) is { } inferred)
        {
            return inferred;
        }

        return UnassignedLevelKey;
    }

    /// <summary>
    /// Nama ruangan sebuah device: Space lebih dulu, Room sebagai cadangan.
    /// </summary>
    /// <remarks>
    /// <b>Space lebih dulu karena Room hampir selalu kosong di model MEP.</b> Ruangan
    /// arsitek datang lewat Revit link, dan <see cref="FamilyInstance.Room"/> hanya
    /// mencari di dokumen instance-nya sendiri — jadi di project seperti itu ia selalu
    /// null. Bukan melempar, bukan kosong sebagian: null seluruhnya, tanpa satu pun
    /// petunjuk bahwa yang salah adalah tempat mencarinya.
    ///
    /// Space adalah padanan Room di sisi MEP dan hidup di model kita sendiri. Saat
    /// ditempatkan dengan link arsitek yang Room Bounding-nya aktif, Revit mengisi
    /// parameter <c>Room Name</c> milik space dari room arsitek itu — jadi namanya sama
    /// dengan yang dilihat engineer di denah, tanpa diketik ulang.
    ///
    /// Urutannya: nama room dari space, lalu room di model sendiri, baru nama space.
    /// Nama space ditaruh terakhir karena space yang ditempatkan sebelum link-nya siap
    /// bernama "Space 12" — konsisten, tapi tidak berarti apa-apa bagi yang membacanya,
    /// dan tidak boleh menutupi nama room yang benar.
    /// </remarks>
    private static string? RoomNameOf(FamilyInstance instance)
    {
        var space = SpaceOf(instance);

        // SPACE_ASSOC_ROOM_NAME, bukan ROOM_NAME: yang kedua nama space itu sendiri,
        // sedangkan yang pertama nama room arsitek yang diasosiasikan Revit ke space —
        // itulah yang cocok dengan nama di denah arsitek.
        return Blank(space is null ? null : Params.String(space, BuiltInParameter.SPACE_ASSOC_ROOM_NAME))
               ?? RoomOwnName(instance)
               ?? Blank(space?.Name);
    }

    private static Space? SpaceOf(FamilyInstance instance)
    {
        try
        {
            return instance.Space;
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException)
        {
            return null;
        }
    }

    private static string? RoomOwnName(FamilyInstance instance)
    {
        try
        {
            return Blank(instance.Room?.Name);
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException)
        {
            // Room hanya bisa dihitung kalau family punya Room Calculation Point;
            // tanpa itu Revit melempar, dan nama ruang memang tidak kita punya.
            return null;
        }
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
