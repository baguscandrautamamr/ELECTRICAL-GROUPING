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
        var devices = new List<DeviceRow>();

        devices.AddRange(ReadDevices(doc, BuiltInCategory.OST_LightingFixtures, DeviceKind.Lighting));
        devices.AddRange(ReadDevices(doc, BuiltInCategory.OST_ElectricalFixtures, DeviceKind.Receptacle));

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

        return new ModelSnapshot { Levels = levels, Panels = panels, Devices = devices };
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

    private static IEnumerable<DeviceRow> ReadDevices(Document doc, BuiltInCategory category, string kind)
    {
        var instances = new FilteredElementCollector(doc)
            .OfCategory(category)
            .WhereElementIsNotElementType()
            .OfClass(typeof(FamilyInstance))
            .Cast<FamilyInstance>();

        foreach (var instance in instances)
        {
            var point = (instance.Location as LocationPoint)?.Point;

            var (status, circuitNumber) = ReadConnection(instance);

            yield return new DeviceRow
            {
                RevitUniqueId = instance.UniqueId,
                Kind = kind,
                LevelKey = LevelKeyOf(doc, instance),
                RoomName = RoomNameOf(instance),
                FamilyKey = FamilyKey.Make(instance.Symbol.Family.Name, instance.Symbol.Name),
                XMm = point is null ? 0 : Units.ToMillimetersRounded(point.X),
                YMm = point is null ? 0 : Units.ToMillimetersRounded(point.Y),
                Va = ApparentLoadVa(instance),
                Status = status,
                CircuitNumber = circuitNumber,
            };
        }
    }

    /// <summary>
    /// Empat status koneksi, ditentukan dari model — bukan dari apa yang pernah
    /// dikirim web. Snapshot berikutnya selalu menang.
    /// </summary>
    private static (string Status, string? CircuitNumber) ReadConnection(FamilyInstance instance)
    {
        if (!HasElectricalConnector(instance))
        {
            return (DeviceStatus.NoConnector, null);
        }

        var systems = instance.MEPModel?.GetElectricalSystems();
        if (systems is null || systems.Count == 0)
        {
            return (DeviceStatus.Unwired, null);
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
            return (DeviceStatus.Connected, Blank(withPanel.CircuitNumber));
        }

        return (DeviceStatus.NoPanel, withoutPanel is null ? null : Blank(withoutPanel.CircuitNumber));
    }

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

    private static string LevelKeyOf(Document doc, FamilyInstance instance)
    {
        if (doc.GetElement(instance.LevelId) is Level level)
        {
            return level.UniqueId;
        }

        // Fixture yang di-host di ceiling sering tidak punya LevelId; levelnya
        // menempel di host.
        if (instance.Host is not null && doc.GetElement(instance.Host.LevelId) is Level hostLevel)
        {
            return hostLevel.UniqueId;
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
