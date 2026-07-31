using System.Text.Json;
using CircuitSync.Core;
using Xunit;

namespace CircuitSync.Core.Tests;

/// <summary>
/// Kontrak data punya tiga cermin: migrasi Postgres, C# di sini, dan TypeScript di web.
/// Test ini menjaga cermin C# — nama kolomnya persis, tidak lebih dan tidak kurang.
/// PostgREST menolak kolom yang tidak dikenal tanpa pesan yang jelas, jadi kesalahan
/// di sini muncul sebagai "sync diam-diam tidak jalan".
/// </summary>
public class ContractTests
{
    private static IReadOnlyCollection<string> Fields<T>(T value) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            JsonSerializer.Serialize(value, CircuitSyncJson.Options))!.Keys;

    [Fact]
    public void Device_serialises_to_the_agreed_columns()
    {
        var fields = Fields(new DeviceRow
        {
            ProjectId = Guid.NewGuid(),
            RevitUniqueId = "abc",
            LevelKey = "L1",
            FamilyKey = "Downlight::18W",
            RoomName = "Ruang Rapat",
            Va = 18,
            CircuitNumber = "(LC)1",
            PanelUniqueId = "panel-1",
        });

        AssertSameSet(
        [
            "project_id", "revit_unique_id", "kind", "level_key", "room_name",
            "family_key", "x_mm", "y_mm", "va", "status", "circuit_number",
            "panel_unique_id",
        ], fields);
    }

    /// <summary>
    /// Write-back setelah apply memakai PATCH, dan PATCH menulis persis kolom yang ada di
    /// body. Kalau kolom geometri ikut masuk ke sini — misalnya karena seseorang
    /// menggantinya kembali dengan <see cref="DeviceRow"/> — posisi device di database
    /// tertimpa nol, dan denah di web berubah jadi setumpuk titik di pojok.
    /// </summary>
    [Fact]
    public void Device_connection_carries_only_the_columns_that_change()
    {
        var fields = Fields(new DeviceConnection
        {
            RevitUniqueId = "abc",
            Status = DeviceStatus.Connected,
            CircuitNumber = "(LC)1",
            PanelUniqueId = "panel-1",
        });

        AssertSameSet(
            ["revit_unique_id", "status", "circuit_number", "panel_unique_id"], fields);
    }

    [Fact]
    public void Layout_device_serialises_to_the_agreed_columns()
    {
        var fields = Fields(new LayoutDeviceRow
        {
            ProjectId = Guid.NewGuid(),
            LayoutUniqueId = "view-1",
            DeviceUniqueId = "lamp-1",
        });

        AssertSameSet(["project_id", "layout_unique_id", "device_unique_id"], fields);
    }

    [Fact]
    public void Layout_lighting_device_serialises_to_the_agreed_columns()
    {
        var fields = Fields(new LayoutLightingDeviceRow
        {
            ProjectId = Guid.NewGuid(),
            LayoutUniqueId = "view-1",
            LightingDeviceUniqueId = "switch-1",
        });

        AssertSameSet(["project_id", "layout_unique_id", "lighting_device_unique_id"], fields);
    }

    [Fact]
    public void Lighting_device_serialises_to_the_agreed_columns()
    {
        var fields = Fields(new LightingDeviceRow
        {
            ProjectId = Guid.NewGuid(),
            RevitUniqueId = "switch-1",
            FamilyKey = "Switch::1 Gang",
            LevelKey = "L1",
            RoomName = "Ruang Rapat",
        });

        AssertSameSet(
        [
            "project_id", "revit_unique_id", "family_key", "level_key", "room_name",
            "x_mm", "y_mm",
        ], fields);
    }

    [Fact]
    public void Wiring_curve_serialises_to_the_agreed_columns()
    {
        var fields = Fields(new WiringCurveRow
        {
            ProjectId = Guid.NewGuid(),
            LayoutUniqueId = "view-1",
            RevitUniqueId = "curve-1",
            SwitchIndex = 1,
        });

        AssertSameSet(
            ["project_id", "layout_unique_id", "revit_unique_id", "switch_index"], fields);
    }

    [Fact]
    public void Line_style_serialises_to_the_agreed_columns()
    {
        var fields = Fields(new LineStyleRow
        {
            ProjectId = Guid.NewGuid(),
            RevitUniqueId = "gs-1",
            Name = "LIGHTING",
            SortOrder = 0,
        });

        AssertSameSet(["project_id", "revit_unique_id", "name", "sort_order"], fields);
    }

    [Fact]
    public void Panel_serialises_to_the_agreed_columns()
    {
        var fields = Fields(new PanelRow
        {
            ProjectId = Guid.NewGuid(),
            RevitUniqueId = "abc",
            Name = "LP-1",
            Prefix = "(LC)",
            DistributionSystem = "380/220V",
            Voltage = "220 V",
            Phase = 3,
            SlotsTotal = 24,
            SlotsUsed = 2,
            IsUsable = true,
        });

        AssertSameSet(
        [
            "project_id", "revit_unique_id", "name", "prefix", "distribution_system",
            "voltage", "phase", "slots_total", "slots_used", "is_usable",
        ], fields);
    }

    [Fact]
    public void Circuit_serialises_to_the_agreed_columns()
    {
        var fields = Fields(new CircuitRow
        {
            Id = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            PanelUniqueId = "panel",
            DeviceUniqueIds = ["a"],
            CircuitNumber = "(LC)1",
            RevitUniqueId = "sys",
            Error = "-",
        });

        AssertSameSet(
        [
            "id", "project_id", "panel_unique_id", "kind", "device_unique_ids",
            "circuit_number", "status", "revit_unique_id", "error",
        ], fields);
    }

    /// <summary>
    /// Null ikut dikirim sebagai null, bukan dibuang. Snapshot adalah pengganti penuh:
    /// device yang room-nya dihapus di Revit harus menjadi null di database, bukan
    /// menyimpan nilai lama selamanya.
    /// </summary>
    [Fact]
    public void Null_columns_are_sent_as_null()
    {
        var fields = Fields(new DeviceRow { RevitUniqueId = "abc", LevelKey = "L1", FamilyKey = "X::Y" });

        Assert.Contains("room_name", fields);
        Assert.Contains("va", fields);
        Assert.Contains("circuit_number", fields);
    }

    /// <summary>
    /// Baris yang isinya lengkap dan baris yang nullable-nya kosong harus menghasilkan
    /// kunci yang sama persis. PostgREST menolak bulk insert dengan objek yang tidak
    /// sekunci lewat <c>400 PGRST102 "All object keys must match"</c>, dan satu device
    /// tanpa room saja sudah cukup menggagalkan seluruh tarikan model.
    /// </summary>
    [Fact]
    public void Devices_with_and_without_nulls_carry_the_same_keys()
    {
        var filled = Fields(new DeviceRow
        {
            RevitUniqueId = "a",
            LevelKey = "L1",
            FamilyKey = "X::Y",
            RoomName = "Ruang Rapat",
            Va = 18,
            CircuitNumber = "(LC)1",
        });

        var bare = Fields(new DeviceRow { RevitUniqueId = "b", LevelKey = "L1", FamilyKey = "X::Y" });

        AssertSameSet(filled.ToArray(), bare);
    }

    [Fact]
    public void Panels_with_and_without_nulls_carry_the_same_keys()
    {
        var filled = Fields(new PanelRow
        {
            RevitUniqueId = "a",
            Name = "LP-1",
            Prefix = "(LC)",
            DistributionSystem = "380/220V",
            Voltage = "220 V",
            Phase = 3,
            SlotsTotal = 24,
            SlotsUsed = 2,
            IsUsable = true,
        });

        // Panel tanpa distribution system: justru bentuk yang paling sering ada di
        // model nyata, dan yang membuat tarikan model gagal seluruhnya.
        var bare = Fields(new PanelRow { RevitUniqueId = "b", Name = "LP-2" });

        AssertSameSet(filled.ToArray(), bare);
    }

    /// <summary>
    /// Patch yang bermaksud mengosongkan kolom harus benar-benar mengirim null —
    /// misalnya membersihkan <c>error</c> saat sebuah job akhirnya berhasil.
    /// </summary>
    [Fact]
    public void Patch_that_clears_a_column_actually_sends_null()
    {
        var json = JsonSerializer.Serialize(
            new { status = "applied", error = (string?)null }, CircuitSyncJson.Options);

        Assert.Contains("\"error\":null", json);
    }

    [Fact]
    public void Sync_job_reads_circuit_ids_from_its_payload()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var job = JsonSerializer.Deserialize<SyncJobRow>($$"""
            {
              "id": "{{Guid.NewGuid()}}",
              "project_id": "{{Guid.NewGuid()}}",
              "direction": "apply",
              "status": "queued",
              "payload": { "circuit_ids": ["{{first}}", "{{second}}"] }
            }
            """, CircuitSyncJson.Options)!;

        Assert.Equal([first, second], job.CircuitIds());
    }

    [Theory]
    [InlineData("""{"circuit_ids": []}""")]
    [InlineData("""{"circuit_ids": "bukan array"}""")]
    [InlineData("""{"lain": 1}""")]
    [InlineData("""{}""")]
    public void Payload_without_usable_circuit_ids_yields_an_empty_list(string payload)
    {
        var job = JsonSerializer.Deserialize<SyncJobRow>($$"""
            {"id":"{{Guid.NewGuid()}}","project_id":"{{Guid.NewGuid()}}",
             "direction":"apply","status":"queued","payload": {{payload}} }
            """, CircuitSyncJson.Options)!;

        Assert.Empty(job.CircuitIds());
    }

    [Fact]
    public void Payload_skips_entries_that_are_not_guids()
    {
        var good = Guid.NewGuid();
        var job = JsonSerializer.Deserialize<SyncJobRow>($$"""
            {"id":"{{Guid.NewGuid()}}","project_id":"{{Guid.NewGuid()}}","direction":"apply",
             "status":"queued","payload": { "circuit_ids": ["bukan-guid", "{{good}}", 7] } }
            """, CircuitSyncJson.Options)!;

        Assert.Equal([good], job.CircuitIds());
    }

    // ---------------------------------------------------------------- wiring

    private static SyncJobRow WiringJob(string payload) =>
        JsonSerializer.Deserialize<SyncJobRow>($$"""
            {"id":"{{Guid.NewGuid()}}","project_id":"{{Guid.NewGuid()}}",
             "direction":"wiring","status":"queued","payload": {{payload}} }
            """, CircuitSyncJson.Options)!;

    [Fact]
    public void Wiring_payload_reads_layout_style_and_points()
    {
        var request = WiringJob("""
            {
              "layout_unique_id": "view-1",
              "line_style_unique_id": "gs-1",
              "runs": [
                {"switch_index": 1, "vertices": [
                  {"x_mm": 1000, "y_mm": 2000},
                  {"x_mm": 1600, "y_mm": 2000},
                  {"x_mm": 1600, "y_mm": 2600}]}
              ]
            }
            """).Wiring();

        Assert.NotNull(request);
        Assert.Equal("view-1", request.LayoutUniqueId);
        Assert.Equal("gs-1", request.LineStyleUniqueId);

        var run = Assert.Single(request.Runs);
        Assert.Equal(1, run.SwitchIndex);
        Assert.Equal(3, run.Vertices.Count);
        Assert.Equal(1600, run.Vertices[2].XMm);
        Assert.Equal(2600, run.Vertices[2].YMm);
    }

    /// <summary>
    /// Nomor saklar boleh tidak ada — ia hanya penanda warna di web, bukan sesuatu yang
    /// menentukan bentuk garis. Nol adalah kaki pertama, dan itu jawaban yang aman.
    /// </summary>
    [Fact]
    public void Wiring_run_without_switch_index_falls_back_to_the_first_leg()
    {
        var request = WiringJob("""
            {"layout_unique_id":"v","line_style_unique_id":"s",
             "runs":[{"vertices":[{"x_mm":0,"y_mm":0},{"x_mm":10,"y_mm":0}]}]}
            """).Wiring();

        Assert.NotNull(request);
        Assert.Equal(0, Assert.Single(request.Runs).SwitchIndex);
    }

    /// <summary>
    /// Satu titik bukan garis. Run seperti itu dibuang, bukan digambar sebagai kurva
    /// berpanjang nol — yang di Revit berarti exception, bukan garis pendek.
    /// </summary>
    [Fact]
    public void Wiring_drops_runs_that_cannot_become_a_line()
    {
        var request = WiringJob("""
            {"layout_unique_id":"v","line_style_unique_id":"s","runs":[
              {"vertices":[{"x_mm":0,"y_mm":0}]},
              {"vertices":[{"x_mm":0,"y_mm":0},{"x_mm":10,"y_mm":0}]},
              {"vertices":[{"x_mm":5,"y_mm":"bukan angka"},{"x_mm":9,"y_mm":9}]}
            ]}
            """).Wiring();

        Assert.NotNull(request);
        Assert.Single(request.Runs);
    }

    /// <summary>
    /// Payload yang tidak lengkap ditolak seluruhnya. Add-in versi lama bisa menerima
    /// bentuk yang belum dikenalnya, dan menggambar dari titik yang setengah terbaca
    /// meninggalkan garis salah di model — yang hanya bisa ditemukan dengan membuka Revit.
    /// </summary>
    [Theory]
    [InlineData("""{"line_style_unique_id":"s","runs":[{"vertices":[{"x_mm":0,"y_mm":0},{"x_mm":1,"y_mm":1}]}]}""")]
    [InlineData("""{"layout_unique_id":"v","runs":[{"vertices":[{"x_mm":0,"y_mm":0},{"x_mm":1,"y_mm":1}]}]}""")]
    [InlineData("""{"layout_unique_id":"","line_style_unique_id":"s","runs":[{"vertices":[{"x_mm":0,"y_mm":0},{"x_mm":1,"y_mm":1}]}]}""")]
    [InlineData("""{"layout_unique_id":"v","line_style_unique_id":"s","runs":[]}""")]
    [InlineData("""{"layout_unique_id":"v","line_style_unique_id":"s","runs":"bukan array"}""")]
    [InlineData("""{"layout_unique_id":"v","line_style_unique_id":"s"}""")]
    [InlineData("""{}""")]
    public void Incomplete_wiring_payload_is_rejected_whole(string payload)
    {
        Assert.Null(WiringJob(payload).Wiring());
    }

    /// <summary>
    /// Job apply dan job wiring memakai kolom payload yang sama. Membaca yang satu
    /// sebagai yang lain harus menghasilkan kosong, bukan tebakan.
    /// </summary>
    [Fact]
    public void Apply_payload_is_not_read_as_a_wiring_request()
    {
        var job = JsonSerializer.Deserialize<SyncJobRow>($$"""
            {"id":"{{Guid.NewGuid()}}","project_id":"{{Guid.NewGuid()}}","direction":"apply",
             "status":"queued","payload": { "circuit_ids": ["{{Guid.NewGuid()}}"] } }
            """, CircuitSyncJson.Options)!;

        Assert.Null(job.Wiring());
        Assert.Single(job.CircuitIds());
    }

    private static void AssertSameSet(string[] expected, IReadOnlyCollection<string> actual)
    {
        Assert.Equal(
            expected.OrderBy(f => f, StringComparer.Ordinal).ToArray(),
            actual.OrderBy(f => f, StringComparer.Ordinal).ToArray());
    }
}
