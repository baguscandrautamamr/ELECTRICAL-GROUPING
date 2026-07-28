using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using CircuitSync.Core;

namespace CircuitSync.Revit;

public sealed record ApplyOptions
{
    /// <summary>Menempatkan tag nomor circuit di samping tiap device yang berhasil di-circuit.</summary>
    public bool PlaceTags { get; init; } = true;

    /// <summary>Jarak tag dari titik device, dalam milimeter.</summary>
    public double TagOffsetMm { get; init; } = 450;
}

public sealed record CircuitApplyResult
{
    public required Guid CircuitId { get; init; }
    public bool Ok { get; init; }

    /// <summary>Nomor yang dihasilkan Revit, misal <c>(LC)1</c>. Kita hanya membacanya.</summary>
    public string? CircuitNumber { get; init; }

    public string? RevitUniqueId { get; init; }

    /// <summary>Kunci pesan untuk UI, bukan teks siap tampil.</summary>
    public string? ErrorKey { get; init; }

    public string? ErrorDetail { get; init; }

    public IReadOnlyList<DeviceRow> UpdatedDevices { get; init; } = [];

    public int TagsPlaced { get; init; }
}

/// <summary>
/// Menerapkan rencana ke model: membuat circuit, menyambung ke panel, menempatkan tag.
/// Tidak pernah menulis nomor circuit — nomor itu hasil <see cref="ElectricalSystem.SelectPanel"/>.
/// </summary>
public static class CircuitApplier
{
    public const string ErrorPanelMissing = "apply.panel_missing";
    public const string ErrorPanelNotFamilyInstance = "apply.panel_not_equipment";
    public const string ErrorNoDevices = "apply.no_devices";
    public const string ErrorCreateFailed = "apply.create_failed";
    public const string ErrorPanelRejected = "apply.panel_rejected";

    /// <summary>
    /// Satu <see cref="TransactionGroup"/> untuk seluruh operasi, lalu
    /// <see cref="TransactionGroup.Assimilate"/>: user bisa membatalkan semuanya dengan
    /// sekali Ctrl+Z. Tiap circuit dibungkus <see cref="SubTransaction"/> sendiri supaya
    /// satu kegagalan tidak menggugurkan sisanya.
    /// </summary>
    public static IReadOnlyList<CircuitApplyResult> Apply(
        Document doc,
        IReadOnlyList<CircuitRow> circuits,
        ApplyOptions? options = null)
    {
        options ??= new ApplyOptions();
        var results = new List<CircuitApplyResult>(circuits.Count);

        using var group = new TransactionGroup(doc, "CircuitSync — terapkan rencana");
        group.Start();

        using (var transaction = new Transaction(doc, "CircuitSync — buat circuit"))
        {
            transaction.Start();

            foreach (var circuit in circuits)
            {
                results.Add(ApplyOne(doc, circuit, options));
            }

            transaction.Commit();
        }

        group.Assimilate();
        return results;
    }

    private static CircuitApplyResult ApplyOne(Document doc, CircuitRow circuit, ApplyOptions options)
    {
        var sub = new SubTransaction(doc);
        sub.Start();

        try
        {
            if (doc.GetElement(circuit.PanelUniqueId) is not FamilyInstance panel)
            {
                sub.RollBack();
                return Failed(circuit, doc.GetElement(circuit.PanelUniqueId) is null
                    ? ErrorPanelMissing
                    : ErrorPanelNotFamilyInstance);
            }

            // Elemen yang sudah dihapus dilaporkan, bukan dilempar.
            var elements = new List<Element>();
            var missing = new List<string>();
            foreach (var uniqueId in circuit.DeviceUniqueIds)
            {
                if (doc.GetElement(uniqueId) is { } element)
                {
                    elements.Add(element);
                }
                else
                {
                    missing.Add(uniqueId);
                }
            }

            if (elements.Count == 0)
            {
                sub.RollBack();
                return Failed(circuit, ErrorNoDevices);
            }

            var ids = elements.Select(e => e.Id).ToList();
            var system = ElectricalSystem.Create(doc, ids, ElectricalSystemType.PowerCircuit);
            if (system is null)
            {
                sub.RollBack();
                return Failed(circuit, ErrorCreateFailed);
            }

            // Nomor circuit muncul dari sini: Revit memilih slot kosong di panel.
            system.SelectPanel(panel);
            doc.Regenerate();

            var number = string.IsNullOrWhiteSpace(system.CircuitNumber) ? null : system.CircuitNumber.Trim();
            var tags = options.PlaceTags ? TagPlacer.Place(doc, elements, options.TagOffsetMm) : 0;

            sub.Commit();

            return new CircuitApplyResult
            {
                CircuitId = circuit.Id,
                Ok = true,
                CircuitNumber = number,
                RevitUniqueId = system.UniqueId,
                ErrorDetail = missing.Count == 0 ? null : $"{missing.Count} device sudah dihapus",
                TagsPlaced = tags,
                UpdatedDevices = elements.Select(e => new DeviceRow
                {
                    RevitUniqueId = e.UniqueId,
                    Status = DeviceStatus.Connected,
                    CircuitNumber = number,
                    // Kolom lain sengaja tidak diisi: upsert hanya menyentuh yang dikirim,
                    // dan sisanya tetap punya snapshot terakhir.
                    Kind = circuit.Kind,
                    LevelKey = "",
                    FamilyKey = "",
                }).ToList(),
            };
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException ex)
        {
            // Penyebab paling sering: panel tanpa distribution system, atau tegangan
            // device tidak cocok dengan panel.
            RollBackQuietly(sub);
            return Failed(circuit, ErrorPanelRejected, ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            RollBackQuietly(sub);
            return Failed(circuit, ErrorCreateFailed, ex.Message);
        }
        finally
        {
            sub.Dispose();
        }
    }

    private static void RollBackQuietly(SubTransaction sub)
    {
        try
        {
            if (sub.HasStarted() && !sub.HasEnded())
            {
                sub.RollBack();
            }
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException)
        {
        }
    }

    private static CircuitApplyResult Failed(CircuitRow circuit, string key, string? detail = null) => new()
    {
        CircuitId = circuit.Id,
        Ok = false,
        ErrorKey = key,
        ErrorDetail = detail,
    };
}
