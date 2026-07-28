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
        });

        AssertSameSet(
        [
            "project_id", "revit_unique_id", "kind", "level_key", "room_name",
            "family_key", "x_mm", "y_mm", "va", "status", "circuit_number",
        ], fields);
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
    /// Null tidak dikirim: kalau ikut terkirim, upsert akan menimpa nilai yang sudah
    /// benar di database dengan null.
    /// </summary>
    [Fact]
    public void Null_columns_are_left_out_of_the_payload()
    {
        var fields = Fields(new DeviceRow { RevitUniqueId = "abc", LevelKey = "L1", FamilyKey = "X::Y" });

        Assert.DoesNotContain("room_name", fields);
        Assert.DoesNotContain("va", fields);
        Assert.DoesNotContain("circuit_number", fields);
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
