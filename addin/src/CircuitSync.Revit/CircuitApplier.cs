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

    /// <summary>
    /// Status koneksi device setelah apply, untuk ditulis balik ke database. Sengaja
    /// bukan baris device penuh — lihat <see cref="DeviceConnection"/>.
    /// </summary>
    public IReadOnlyList<DeviceConnection> UpdatedDevices { get; init; } = [];

    public int TagsPlaced { get; init; }

    /// <summary>Benar kalau circuit lama dibongkar dulu karena isinya diubah dari web.</summary>
    public bool Rebuilt { get; init; }
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

            // Circuit yang isinya diubah dari web membawa UniqueId ElectricalSystem lamanya.
            // Revit tidak mengizinkan satu device berada di dua power circuit, jadi yang lama
            // harus dibongkar dulu — kalau tidak, Create menolak seluruh rencana.
            var rebuilt = RemoveExistingSystem(doc, circuit.RevitUniqueId);

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
                Rebuilt = rebuilt,
                UpdatedDevices = elements.Select(e => new DeviceConnection
                {
                    RevitUniqueId = e.UniqueId,
                    Status = DeviceStatus.Connected,
                    CircuitNumber = number,
                }).ToList(),
            };
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException ex)
        {
            // Penyebab paling sering: panel penuh, tegangan device tidak cocok dengan
            // distribution system panel, atau jumlah pole tidak terlayani.
            //
            // Pesan Revit-lah satu-satunya yang membedakan ketiganya, jadi ia dibawa
            // apa adanya — beserta keadaan panel saat ditolak. Tanpa itu yang sampai ke
            // user hanya "ditolak Revit", yang tidak memberitahu apa pun tentang apa
            // yang harus diperbaiki.
            RollBackQuietly(sub);
            return Failed(circuit, ErrorPanelRejected, $"{ex.Message} — {Describe(doc, circuit)}");
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

    /// <summary>
    /// Membongkar ElectricalSystem lama sebelum circuit dibuat ulang dengan isi baru.
    /// Mengembalikan false kalau tidak ada yang perlu dibongkar — circuit baru, atau
    /// circuit-nya sudah dihapus orang lain dari Revit.
    /// </summary>
    /// <remarks>
    /// Nomor circuit hasil pembangunan ulang <b>tidak dijamin sama</b>: Revit memilih
    /// slot kosong sendiri saat <see cref="ElectricalSystem.SelectPanel"/>, dan slot
    /// yang baru saja dilepas belum tentu yang dipakainya lagi.
    /// </remarks>
    private static bool RemoveExistingSystem(Document doc, string? systemUniqueId)
    {
        if (string.IsNullOrEmpty(systemUniqueId) || doc.GetElement(systemUniqueId) is not ElectricalSystem existing)
        {
            return false;
        }

        doc.Delete(existing.Id);
        doc.Regenerate();
        return true;
    }

    /// <summary>
    /// Keadaan panel saat sebuah circuit ditolak: nama, slot terpakai, dan berapa titik
    /// yang hendak disambungkan. Tiga angka itu biasanya sudah cukup membedakan "panel
    /// penuh" dari "tegangan tidak cocok" tanpa perlu membuka Revit.
    /// </summary>
    private static string Describe(Document doc, CircuitRow circuit)
    {
        if (doc.GetElement(circuit.PanelUniqueId) is not FamilyInstance panel)
        {
            return $"{circuit.DeviceUniqueIds.Length} titik";
        }

        var name = Params.String(panel, BuiltInParameter.RBS_ELEC_PANEL_NAME) ?? panel.Name;
        var used = panel.MEPModel?.GetAssignedElectricalSystems()?.Count;
        var total = Params.TypeInt(doc, panel, BuiltInParameter.RBS_ELEC_MAX_POLE_BREAKERS);

        var slots = used is null && total is null
            ? "slot tidak diketahui"
            : $"slot {used?.ToString() ?? "?"}/{total?.ToString() ?? "?"}";

        return $"panel {name}, {slots}, {circuit.DeviceUniqueIds.Length} titik";
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
