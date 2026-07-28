using CircuitSync.Core;
using Xunit;

namespace CircuitSync.Core.Tests;

public class FamilyKeyTests
{
    [Fact]
    public void Joins_family_and_type()
    {
        Assert.Equal("Downlight::18W", FamilyKey.Make("Downlight", "18W"));
    }

    [Fact]
    public void Trims_surrounding_whitespace()
    {
        Assert.Equal("Downlight::18W", FamilyKey.Make("  Downlight ", " 18W  "));
    }

    /// <summary>
    /// Nama family di Revit boleh berisi titik dua. Kalau dibiarkan, key akan punya
    /// dua pemisah dan mapping simbol di web memilih potongan yang salah.
    /// </summary>
    [Fact]
    public void Collapses_a_separator_inside_a_name()
    {
        var key = FamilyKey.Make("Lampu::Sorot", "18W");

        Assert.Equal("Lampu Sorot::18W", key);
        Assert.True(FamilyKey.TryParse(key, out var family, out var type));
        Assert.Equal("Lampu Sorot", family);
        Assert.Equal("18W", type);
    }

    [Fact]
    public void Round_trips()
    {
        Assert.True(FamilyKey.TryParse(FamilyKey.Make("A", "B"), out var family, out var type));
        Assert.Equal("A", family);
        Assert.Equal("B", type);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("tanpa pemisah")]
    public void Rejects_input_without_a_separator(string? input)
    {
        Assert.False(FamilyKey.TryParse(input, out _, out _));
    }

    [Fact]
    public void Handles_empty_names_without_throwing()
    {
        Assert.Equal("::", FamilyKey.Make(null, null));
    }
}
