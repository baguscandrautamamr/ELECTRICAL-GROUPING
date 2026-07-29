using CircuitSync.Core;
using Xunit;

namespace CircuitSync.Core.Tests;

public class PlanValidatorTests
{
    private static DeviceRow Device(string id, string kind = DeviceKind.Lighting,
        string status = DeviceStatus.Unwired) => new()
    {
        RevitUniqueId = id,
        Kind = kind,
        Status = status,
        LevelKey = "L1",
        FamilyKey = "Downlight::18W",
        Va = 18,
    };

    private static PanelRow Panel(string id, bool usable = true) => new()
    {
        RevitUniqueId = id,
        Name = "LP-1",
        IsUsable = usable,
    };

    private static CircuitRow Circuit(params string[] devices) => new()
    {
        Id = Guid.NewGuid(),
        PanelUniqueId = "panel-1",
        Kind = DeviceKind.Lighting,
        DeviceUniqueIds = devices,
    };

    private static ModelSnapshot Snapshot(IEnumerable<DeviceRow> devices, IEnumerable<PanelRow>? panels = null) => new()
    {
        Devices = devices.ToList(),
        Panels = (panels ?? [Panel("panel-1")]).ToList(),
    };

    [Fact]
    public void Accepts_a_plain_circuit()
    {
        var snapshot = Snapshot([Device("a"), Device("b")]);

        var result = PlanValidator.Validate([Circuit("a", "b")], snapshot);

        Assert.True(result.AllAccepted);
        Assert.Single(result.Accepted);
    }

    [Fact]
    public void Rejects_a_circuit_with_no_devices()
    {
        var result = PlanValidator.Validate([Circuit()], Snapshot([]));

        Assert.Empty(result.Accepted);
        Assert.Contains(result.Problems, p => p.MessageKey == PlanValidator.EmptyCircuit);
    }

    [Fact]
    public void Rejects_a_device_that_no_longer_exists()
    {
        var result = PlanValidator.Validate([Circuit("a", "hilang")], Snapshot([Device("a")]));

        Assert.Empty(result.Accepted);
        Assert.Contains(result.Problems, p => p.MessageKey == PlanValidator.UnknownDevice && p.Detail == "hilang");
    }

    [Fact]
    public void Rejects_a_panel_without_a_distribution_system()
    {
        var snapshot = Snapshot([Device("a")], [Panel("panel-1", usable: false)]);

        var result = PlanValidator.Validate([Circuit("a")], snapshot);

        Assert.Contains(result.Problems, p => p.MessageKey == PlanValidator.PanelNotUsable);
    }

    [Fact]
    public void Rejects_a_panel_that_was_deleted()
    {
        var snapshot = Snapshot([Device("a")], []);

        var result = PlanValidator.Validate([Circuit("a")], snapshot);

        Assert.Contains(result.Problems, p => p.MessageKey == PlanValidator.UnknownPanel);
    }

    [Fact]
    public void Rejects_a_device_without_an_electrical_connector()
    {
        var snapshot = Snapshot([Device("a", status: DeviceStatus.NoConnector)]);

        var result = PlanValidator.Validate([Circuit("a")], snapshot);

        Assert.Contains(result.Problems, p => p.MessageKey == PlanValidator.DeviceWithoutConnector);
    }

    [Fact]
    public void Rejects_a_device_already_on_another_circuit()
    {
        var snapshot = Snapshot([Device("a", status: DeviceStatus.Connected)]);

        var result = PlanValidator.Validate([Circuit("a")], snapshot);

        Assert.Contains(result.Problems, p => p.MessageKey == PlanValidator.AlreadyConnected);
    }

    [Fact]
    public void Rejects_a_receptacle_inside_a_lighting_circuit()
    {
        var snapshot = Snapshot([Device("a"), Device("b", kind: DeviceKind.Receptacle)]);

        var result = PlanValidator.Validate([Circuit("a", "b")], snapshot);

        Assert.Contains(result.Problems, p => p.MessageKey == PlanValidator.KindMismatch);
    }

    /// <summary>
    /// Circuit kedua yang memakai device yang sama ditolak, tapi yang pertama tetap
    /// jalan — satu rencana bermasalah tidak boleh menggugurkan sisanya.
    /// </summary>
    [Fact]
    public void Rejects_only_the_second_circuit_claiming_the_same_device()
    {
        var snapshot = Snapshot([Device("a"), Device("b")]);
        var first = Circuit("a", "b");
        var second = Circuit("b");

        var result = PlanValidator.Validate([first, second], snapshot);

        Assert.Single(result.Accepted);
        Assert.Equal(first.Id, result.Accepted[0].Id);
        Assert.Contains(result.Problems,
            p => p.CircuitId == second.Id && p.MessageKey == PlanValidator.DeviceInTwoCircuits);
    }

    [Fact]
    public void Keeps_healthy_circuits_when_another_one_fails()
    {
        var snapshot = Snapshot([Device("a"), Device("b")]);
        var good = Circuit("a");
        var bad = Circuit("tidak-ada");

        var result = PlanValidator.Validate([bad, good], snapshot);

        Assert.Single(result.Accepted);
        Assert.Equal(good.Id, result.Accepted[0].Id);
        Assert.False(result.AllAccepted);
    }

    // ------------------------------------------------------------------ mengubah circuit

    /// <summary>
    /// Circuit yang sudah diterapkan lalu diubah dari web: device di dalamnya memang
    /// berstatus <c>connected</c>, tapi ke circuit ini sendiri. Menolaknya berarti circuit
    /// yang sudah jalan tidak akan pernah bisa diubah lagi.
    /// </summary>
    [Fact]
    public void Accepts_devices_already_on_the_circuit_being_rebuilt()
    {
        var snapshot = Snapshot([
            Device("a", status: DeviceStatus.Connected),
            Device("b", status: DeviceStatus.Connected),
        ]) with
        {
            DeviceSystems = new Dictionary<string, string> { ["a"] = "system-1", ["b"] = "system-1" },
        };

        var circuit = Circuit("a", "b") with { RevitUniqueId = "system-1" };

        var result = PlanValidator.Validate([circuit], snapshot);

        Assert.True(result.AllAccepted);
        Assert.Single(result.Accepted);
    }

    /// <summary>
    /// Menambahkan device yang masih dipakai circuit <b>lain</b> tetap ditolak, sekalipun
    /// yang sedang diubah adalah circuit yang sudah ada.
    /// </summary>
    [Fact]
    public void Rejects_a_device_owned_by_a_different_system_while_rebuilding()
    {
        var snapshot = Snapshot([
            Device("a", status: DeviceStatus.Connected),
            Device("b", status: DeviceStatus.Connected),
        ]) with
        {
            DeviceSystems = new Dictionary<string, string> { ["a"] = "system-1", ["b"] = "system-2" },
        };

        var circuit = Circuit("a", "b") with { RevitUniqueId = "system-1" };

        var result = PlanValidator.Validate([circuit], snapshot);

        Assert.Empty(result.Accepted);
        Assert.Contains(result.Problems, p => p.MessageKey == PlanValidator.AlreadyConnected && p.Detail == "b");
    }

    /// <summary>
    /// Device yang dikeluarkan dari circuit tidak perlu ikut dikirim: yang dikirim web
    /// adalah isi barunya, dan sisanya dilepas add-in saat circuit dibangun ulang.
    /// </summary>
    [Fact]
    public void Accepts_a_rebuilt_circuit_that_dropped_a_device()
    {
        var snapshot = Snapshot([
            Device("a", status: DeviceStatus.Connected),
            Device("b", status: DeviceStatus.Connected),
        ]) with
        {
            DeviceSystems = new Dictionary<string, string> { ["a"] = "system-1", ["b"] = "system-1" },
        };

        var result = PlanValidator.Validate([Circuit("a") with { RevitUniqueId = "system-1" }], snapshot);

        Assert.True(result.AllAccepted);
        Assert.Equal(["a"], result.Accepted[0].DeviceUniqueIds);
    }
}
