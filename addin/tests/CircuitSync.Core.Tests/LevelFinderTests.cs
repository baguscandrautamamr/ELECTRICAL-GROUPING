using CircuitSync.Core;
using Xunit;

namespace CircuitSync.Core.Tests;

public class LevelFinderTests
{
    private static LevelRow Level(string key, double elevationMm) => new()
    {
        LevelKey = key,
        Name = key,
        ElevationMm = elevationMm,
    };

    private static readonly LevelRow[] Building =
    [
        Level("GF", 0),
        Level("L1", 4000),
        Level("L2", 8000),
    ];

    [Fact]
    public void Nothing_to_choose_from_gives_nothing()
    {
        Assert.Null(LevelFinder.KeyFor([], 1500));
    }

    /// <summary>
    /// Stop kontak dinding di 300 mm milik lantai tempat dindingnya berdiri.
    /// </summary>
    [Fact]
    public void Socket_just_above_the_floor_belongs_to_that_floor()
    {
        Assert.Equal("GF", LevelFinder.KeyFor(Building, 300));
        Assert.Equal("L1", LevelFinder.KeyFor(Building, 4300));
    }

    /// <summary>
    /// Yang dicari level di bawahnya, bukan elevasi terdekat. Saklar di 3.700 mm masih
    /// milik lantai di kakinya — mencari yang terdekat akan memilih lantai di atas
    /// kepalanya.
    /// </summary>
    [Fact]
    public void High_mounted_device_stays_on_the_floor_below_it()
    {
        Assert.Equal("GF", LevelFinder.KeyFor(Building, 3500));
    }

    [Fact]
    public void Device_exactly_on_a_level_belongs_to_it()
    {
        Assert.Equal("L1", LevelFinder.KeyFor(Building, 4000));
    }

    /// <summary>
    /// Stop kontak lantai yang tertanam sedikit di bawah pelat tetap milik level itu,
    /// bukan level di bawahnya.
    /// </summary>
    [Fact]
    public void Slightly_recessed_device_belongs_to_the_level_above_it()
    {
        Assert.Equal("L1", LevelFinder.KeyFor(Building, 4000 - LevelFinder.ToleranceMm + 1));
    }

    /// <summary>
    /// Titik di bawah level terendah — basement yang tidak dimodelkan, atau koordinat
    /// yang meleset. Level terendah lebih berguna daripada menyerah dan menyembunyikan
    /// device dari denah.
    /// </summary>
    [Fact]
    public void Below_everything_falls_back_to_the_lowest_level()
    {
        Assert.Equal("GF", LevelFinder.KeyFor(Building, -9000));
    }

    [Fact]
    public void Level_order_in_the_list_does_not_matter()
    {
        LevelRow[] shuffled = [Level("L2", 8000), Level("GF", 0), Level("L1", 4000)];

        Assert.Equal("L1", LevelFinder.KeyFor(shuffled, 5000));
    }
}
