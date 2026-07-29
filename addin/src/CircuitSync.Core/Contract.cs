using System.Text.Json;
using System.Text.Json.Serialization;

namespace CircuitSync.Core;

/// <summary>
/// Nilai <c>kind</c>: dasar pemisahan tab dan circuit.
/// </summary>
public static class DeviceKind
{
    public const string Lighting = "lighting";
    public const string Receptacle = "receptacle";

    public static readonly string[] All = [Lighting, Receptacle];

    public static bool IsValid(string? value) => value is Lighting or Receptacle;
}

/// <summary>
/// Empat status koneksi device. Dipakai sama persis di web.
/// </summary>
public static class DeviceStatus
{
    /// <summary>Belum di-circuit.</summary>
    public const string Unwired = "unwired";

    /// <summary>Sudah circuit tapi panel kosong.</summary>
    public const string NoPanel = "no_panel";

    /// <summary>Circuit dan panel terisi.</summary>
    public const string Connected = "connected";

    /// <summary>Tidak punya connector listrik — tidak bisa dipilih.</summary>
    public const string NoConnector = "no_connector";

    public static readonly string[] All = [Unwired, NoPanel, Connected, NoConnector];
}

public static class CircuitStatus
{
    public const string Draft = "draft";
    public const string Queued = "queued";
    public const string Applied = "applied";
    public const string Failed = "failed";
}

public static class SyncDirection
{
    /// <summary>Web mengantre rencana untuk diterapkan add-in.</summary>
    public const string Apply = "apply";

    /// <summary>Add-in mencatat tarikan model ke cloud.</summary>
    public const string Snapshot = "snapshot";
}

public static class SyncJobStatus
{
    public const string Queued = "queued";
    public const string Applied = "applied";
    public const string Failed = "failed";
}

public sealed record ProjectRow
{
    [JsonPropertyName("id")] public Guid Id { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("owner_id")] public Guid? OwnerId { get; init; }
    [JsonPropertyName("updated_at")] public DateTimeOffset? UpdatedAt { get; init; }
}

public sealed record LevelRow
{
    [JsonPropertyName("project_id")] public Guid ProjectId { get; init; }
    [JsonPropertyName("level_key")] public string LevelKey { get; init; } = "";
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("elevation_mm")] public double ElevationMm { get; init; }
    [JsonPropertyName("sort_order")] public int SortOrder { get; init; }
}

/// <summary>
/// Cerminan satu view denah Revit. Ini yang jadi halaman kerja di web, menggantikan
/// pasangan (level, kind) — nama view sudah memuat keduanya, dan hanya view yang
/// membawa crop region serta skala yang benar.
/// </summary>
public sealed record LayoutRow
{
    [JsonPropertyName("project_id")] public Guid ProjectId { get; init; }
    [JsonPropertyName("revit_unique_id")] public string RevitUniqueId { get; init; } = "";
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("kind")] public string Kind { get; init; } = DeviceKind.Lighting;
    [JsonPropertyName("level_key")] public string LevelKey { get; init; } = "";

    /// <summary>Penyebut skala Revit: 1:100 disimpan sebagai 100.</summary>
    [JsonPropertyName("scale")] public int? Scale { get; init; }

    [JsonPropertyName("crop_min_x_mm")] public double? CropMinXMm { get; init; }
    [JsonPropertyName("crop_min_y_mm")] public double? CropMinYMm { get; init; }
    [JsonPropertyName("crop_max_x_mm")] public double? CropMaxXMm { get; init; }
    [JsonPropertyName("crop_max_y_mm")] public double? CropMaxYMm { get; init; }

    [JsonPropertyName("sort_order")] public int SortOrder { get; init; }
}

public sealed record DeviceRow
{
    [JsonPropertyName("project_id")] public Guid ProjectId { get; init; }
    [JsonPropertyName("revit_unique_id")] public string RevitUniqueId { get; init; } = "";
    [JsonPropertyName("kind")] public string Kind { get; init; } = DeviceKind.Lighting;
    [JsonPropertyName("level_key")] public string LevelKey { get; init; } = "";
    [JsonPropertyName("room_name")] public string? RoomName { get; init; }
    [JsonPropertyName("family_key")] public string FamilyKey { get; init; } = "";
    [JsonPropertyName("x_mm")] public double XMm { get; init; }
    [JsonPropertyName("y_mm")] public double YMm { get; init; }
    [JsonPropertyName("va")] public double? Va { get; init; }
    [JsonPropertyName("status")] public string Status { get; init; } = DeviceStatus.Unwired;
    [JsonPropertyName("circuit_number")] public string? CircuitNumber { get; init; }
}

public sealed record PanelRow
{
    [JsonPropertyName("project_id")] public Guid ProjectId { get; init; }
    [JsonPropertyName("revit_unique_id")] public string RevitUniqueId { get; init; } = "";
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("prefix")] public string? Prefix { get; init; }
    [JsonPropertyName("distribution_system")] public string? DistributionSystem { get; init; }
    [JsonPropertyName("voltage")] public string? Voltage { get; init; }
    [JsonPropertyName("phase")] public int? Phase { get; init; }
    [JsonPropertyName("slots_total")] public int? SlotsTotal { get; init; }
    [JsonPropertyName("slots_used")] public int? SlotsUsed { get; init; }
    [JsonPropertyName("is_usable")] public bool IsUsable { get; init; }
}

public sealed record CircuitRow
{
    [JsonPropertyName("id")] public Guid Id { get; init; }
    [JsonPropertyName("project_id")] public Guid ProjectId { get; init; }
    [JsonPropertyName("panel_unique_id")] public string PanelUniqueId { get; init; } = "";
    [JsonPropertyName("kind")] public string Kind { get; init; } = DeviceKind.Lighting;
    [JsonPropertyName("device_unique_ids")] public string[] DeviceUniqueIds { get; init; } = [];
    [JsonPropertyName("circuit_number")] public string? CircuitNumber { get; init; }
    [JsonPropertyName("status")] public string Status { get; init; } = CircuitStatus.Draft;
    [JsonPropertyName("revit_unique_id")] public string? RevitUniqueId { get; init; }
    [JsonPropertyName("error")] public string? Error { get; init; }
}

public sealed record SyncJobRow
{
    [JsonPropertyName("id")] public Guid Id { get; init; }
    [JsonPropertyName("project_id")] public Guid ProjectId { get; init; }
    [JsonPropertyName("direction")] public string Direction { get; init; } = SyncDirection.Apply;
    [JsonPropertyName("status")] public string Status { get; init; } = SyncJobStatus.Queued;
    [JsonPropertyName("payload")] public JsonElement Payload { get; init; }
    [JsonPropertyName("error")] public string? Error { get; init; }
    [JsonPropertyName("applied_at")] public DateTimeOffset? AppliedAt { get; init; }

    /// <summary>
    /// Daftar circuit yang harus diterapkan. Payload sengaja jsonb supaya bentuknya
    /// bisa tumbuh tanpa migrasi; yang wajib ada cuma <c>circuit_ids</c>.
    /// </summary>
    public IReadOnlyList<Guid> CircuitIds()
    {
        if (Payload.ValueKind != JsonValueKind.Object ||
            !Payload.TryGetProperty("circuit_ids", out var ids) ||
            ids.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<Guid>();
        foreach (var item in ids.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && Guid.TryParse(item.GetString(), out var id))
            {
                result.Add(id);
            }
        }

        return result;
    }
}

public sealed record SymbolOverrideRow
{
    [JsonPropertyName("project_id")] public Guid ProjectId { get; init; }
    [JsonPropertyName("family_key")] public string FamilyKey { get; init; } = "";
    [JsonPropertyName("symbol")] public string Symbol { get; init; } = "";
}

/// <summary>
/// Sekali tarik seluruh device, panel, dan level dari satu dokumen Revit.
/// </summary>
public sealed record ModelSnapshot
{
    public IReadOnlyList<LevelRow> Levels { get; init; } = [];
    public IReadOnlyList<LayoutRow> Layouts { get; init; } = [];
    public IReadOnlyList<PanelRow> Panels { get; init; } = [];
    public IReadOnlyList<DeviceRow> Devices { get; init; } = [];

    /// <summary>Family yang ditemukan di model, untuk mapping simbol 2D di web.</summary>
    public IReadOnlyList<string> FamilyKeys =>
        Devices.Select(d => d.FamilyKey).Distinct().OrderBy(k => k, StringComparer.Ordinal).ToArray();
}

public static class CircuitSyncJson
{
    /// <summary>
    /// Satu-satunya opsi serialisasi yang dipakai untuk bicara dengan PostgREST.
    /// Snake case sudah eksplisit lewat atribut; kebijakan di sini menjaga field
    /// baru yang lupa diberi atribut tetap ikut aturan.
    /// </summary>
    /// <remarks>
    /// Null <b>ikut ditulis</b>, dan itu disengaja karena dua hal.
    ///
    /// Pertama, PostgREST menolak bulk insert yang objeknya tidak sekunci dengan
    /// <c>400 PGRST102 "All object keys must match"</c>. Kalau null dibuang, device
    /// yang punya <c>room_name</c> dan yang tidak menghasilkan bentuk objek berbeda
    /// di dalam satu array, dan seluruh tarikan model gagal — tanpa satu pun baris
    /// masuk, dan tanpa pesan yang menyebut kolom mana.
    ///
    /// Kedua, snapshot adalah pengganti penuh: model Revit yang jadi sumber
    /// kebenaran untuk kolom-kolom ini. Device yang room-nya dihapus di Revit harus
    /// menjadi null di database. Membuang null justru membuat nilai lama menetap
    /// selamanya, dan membuat patch yang bermaksud mengosongkan kolom — seperti
    /// membersihkan <c>error</c> saat job berhasil — diam-diam tidak berefek.
    /// </remarks>
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };
}
