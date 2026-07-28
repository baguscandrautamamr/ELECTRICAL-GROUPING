using CircuitSync.Core;
using Xunit;

namespace CircuitSync.Core.Tests;

public class LoadSummaryTests
{
    private static DeviceRow Device(string id, double? va) => new()
    {
        RevitUniqueId = id,
        LevelKey = "L1",
        FamilyKey = "X::Y",
        Va = va,
    };

    [Fact]
    public void Adds_up_apparent_load()
    {
        var summary = LoadSummary.For([Device("a", 18), Device("b", 36)]);

        Assert.Equal(2, summary.DeviceCount);
        Assert.Equal(54, summary.TotalVa);
        Assert.Equal(0, summary.MissingVaCount);
    }

    /// <summary>
    /// Device tanpa VA dihitung terpisah, bukan dianggap nol diam-diam: total yang
    /// kelihatan meyakinkan padahal setengah datanya hilang lebih berbahaya daripada
    /// total yang jelas bertanda.
    /// </summary>
    [Fact]
    public void Counts_devices_without_a_load_separately()
    {
        var summary = LoadSummary.For([Device("a", 18), Device("b", null), Device("c", 0)]);

        Assert.Equal(3, summary.DeviceCount);
        Assert.Equal(18, summary.TotalVa);
        Assert.Equal(2, summary.MissingVaCount);
    }

    [Fact]
    public void Derives_current_from_voltage()
    {
        var summary = LoadSummary.For([Device("a", 440)]);

        Assert.Equal(2, summary.AmpsAt(220));
    }

    [Fact]
    public void Unknown_voltage_gives_zero_instead_of_dividing_by_zero()
    {
        var summary = LoadSummary.For([Device("a", 440)]);

        Assert.Equal(0, summary.AmpsAt(0));
        Assert.Equal(0, summary.AmpsAt(-1));
    }

    [Fact]
    public void Ignores_devices_that_are_no_longer_in_the_snapshot()
    {
        var snapshot = new ModelSnapshot { Devices = [Device("a", 18)] };
        var circuit = new CircuitRow { Id = Guid.NewGuid(), DeviceUniqueIds = ["a", "sudah-dihapus"] };

        var summary = LoadSummary.For(circuit, snapshot);

        Assert.Equal(1, summary.DeviceCount);
        Assert.Equal(18, summary.TotalVa);
    }

    [Fact]
    public void Empty_circuit_totals_zero()
    {
        var summary = LoadSummary.For([]);

        Assert.Equal(0, summary.DeviceCount);
        Assert.Equal(0, summary.TotalVa);
    }

    [Fact]
    public void Snapshot_lists_family_keys_once_each_in_order()
    {
        var snapshot = new ModelSnapshot
        {
            Devices =
            [
                new DeviceRow { RevitUniqueId = "a", LevelKey = "L1", FamilyKey = "B::1" },
                new DeviceRow { RevitUniqueId = "b", LevelKey = "L1", FamilyKey = "A::1" },
                new DeviceRow { RevitUniqueId = "c", LevelKey = "L1", FamilyKey = "B::1" },
            ],
        };

        Assert.Equal(["A::1", "B::1"], snapshot.FamilyKeys);
    }
}
