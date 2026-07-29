using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
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

        var (layouts, layoutDevices) = ReadLayouts(doc);

        return new ModelSnapshot
        {
            Levels = levels,
            Layouts = layouts,
            Panels = panels,
            Devices = devices,
            LayoutDevices = layoutDevices,
            DeviceSystems = systems,
        };
    }

    /// <summary>
    /// View denah yang dipakai sebagai halaman kerja di web. Yang memutuskan view mana
    /// yang ikut adalah <see cref="LayoutFilter"/> di Core; di sini hanya penerjemahan.
    /// </summary>
    private static (List<LayoutRow> Rows, List<LayoutDeviceRow> Members) ReadLayouts(Document doc)
    {
        var rows = new List<LayoutRow>();
        var members = new List<LayoutDeviceRow>();

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

            foreach (var deviceId in VisibleDeviceIds(doc, view, kind))
            {
                members.Add(new LayoutDeviceRow
                {
                    LayoutUniqueId = view.UniqueId,
                    DeviceUniqueId = deviceId,
                });
            }
        }

        return (rows
            .OrderBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            .Select((row, index) => row with { SortOrder = index })
            .ToList(), members);
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
    private static IEnumerable<string> VisibleDeviceIds(Document doc, View view, string kind)
    {
        var category = kind == DeviceKind.Receptacle
            ? BuiltInCategory.OST_ElectricalFixtures
            : BuiltInCategory.OST_LightingFixtures;

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

    private static string? RoomNameOf(FamilyInstance instance)
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
