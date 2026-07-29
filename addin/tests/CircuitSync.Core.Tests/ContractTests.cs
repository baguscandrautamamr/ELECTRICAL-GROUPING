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

    private static void AssertSameSet(string[] expected, IReadOnlyCollection<string> actual)
    {
        Assert.Equal(
            expected.OrderBy(f => f, StringComparer.Ordinal).ToArray(),
            actual.OrderBy(f => f, StringComparer.Ordinal).ToArray());
    }
}
