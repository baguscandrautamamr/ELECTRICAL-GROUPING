using CircuitSync.Core;
using Xunit;

namespace CircuitSync.Core.Tests;

/// <summary>
/// Nama view diambil apa adanya dari model sungguhan (lihat daftar view di
/// CATATAN.md), supaya test ini gagal kalau penandanya diubah tanpa sadar.
/// </summary>
public class LayoutFilterTests
{
    [Theory]
    [InlineData("D_FG WAREHOUSE GROUND FLOOR - LIGHTING SYSTEM LAYOUT PLAN")]
    [InlineData("D_FG OFFICE FIRST FLOOR - LIGHTING SYSTEM LAYOUT PLAN")]
    [InlineData("lantai 2 - lighting")]
    public void Lighting_layouts_are_recognised(string name) =>
        Assert.Equal(DeviceKind.Lighting, LayoutFilter.KindOf(name));

    [Theory]
    [InlineData("D_FG WAREHOUSE GROUND FLOOR - SMALL POWER SUPPLY & SOCKET OUTLET SYSTEM LAYOUT PLAN")]
    [InlineData("D_FG OFFICE GROUND FLOOR - SMALL POWER SUPPLY & SOCKET OUTLET SYSTEM LAYOUT PLAN")]
    [InlineData("Denah receptacle lantai 1")]
    public void Receptacle_layouts_are_recognised(string name) =>
        Assert.Equal(DeviceKind.Receptacle, LayoutFilter.KindOf(name));

    /// <summary>
    /// Denah sistem lain ikut ada di model dan jumlahnya jauh lebih banyak. Kalau
    /// ikut terkirim, daftar layout di web jadi tidak terpakai.
    /// </summary>
    [Theory]
    [InlineData("D_FG WAREHOUSE GROUND FLOOR - FIRE ALARM SYSTEM LAYOUT PLAN")]
    [InlineData("D_FG OFFICE FIRST FLOOR - CCTV & ACCESS CARD SYSTEM LAYOUT PLAN")]
    [InlineData("D_FG WAREHOUSE GROUND FLOOR - GROUNDING SYSTEM LAYOUT PLAN")]
    [InlineData("D_FG OFFICE GROUND FLOOR - CABLE CONTAINMENT SYSTEM LAYOUT PLAN")]
    [InlineData("D_FG WAREHOUSE GROUND FLOOR - PUBLIC ADDRESS SYSTEM LAYOUT PLAN")]
    [InlineData("Level 1")]
    [InlineData("")]
    [InlineData(null)]
    public void Other_views_are_left_out(string? name) => Assert.Null(LayoutFilter.KindOf(name));

    /// <summary>
    /// Nama yang menyebut keduanya harus jatuh ke satu sisi saja, bukan terhitung dua
    /// kali. Receptacle menang karena penandanya lebih spesifik.
    /// </summary>
    [Fact]
    public void A_name_mentioning_both_counts_once_as_receptacle() =>
        Assert.Equal(DeviceKind.Receptacle, LayoutFilter.KindOf("SMALL POWER & LIGHTING LAYOUT PLAN"));
}
