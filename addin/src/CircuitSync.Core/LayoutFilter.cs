namespace CircuitSync.Core;

/// <summary>
/// Memutuskan apakah sebuah view denah Revit termasuk layout kerja, dan kalau ya untuk
/// jenis device apa.
///
/// Hidup di Core, bukan di lapisan Revit, karena ini keputusan — bukan penerjemahan —
/// dan keputusan harus bisa dites tanpa Revit. Lapisan Revit cukup menyerahkan nama
/// view-nya.
/// </summary>
public static class LayoutFilter
{
    /// <summary>
    /// Disiplin penamaan yang dipakai model: setiap sistem punya satu denah sendiri,
    /// misalnya "D_FG WAREHOUSE GROUND FLOOR - LIGHTING SYSTEM LAYOUT PLAN" dan
    /// "D_FG OFFICE FIRST FLOOR - SMALL POWER SUPPLY &amp; SOCKET OUTLET SYSTEM LAYOUT PLAN".
    ///
    /// Denah sistem lain — fire alarm, CCTV, public address, grounding, cable
    /// containment — tidak ada urusannya dengan pengelompokan circuit dan sengaja
    /// tidak ikut terkirim.
    /// </summary>
    private static readonly string[] ReceptacleMarkers =
        ["SOCKET OUTLET", "SMALL POWER", "RECEPTACLE", "POWER OUTLET"];

    private static readonly string[] LightingMarkers = ["LIGHTING", "LAMP"];

    /// <summary>
    /// <see cref="DeviceKind"/> untuk view ini, atau null kalau view-nya bukan layout
    /// kerja. Receptacle diperiksa lebih dulu karena penandanya lebih spesifik;
    /// nama seperti "SMALL POWER &amp; LIGHTING" akan jatuh ke receptacle, bukan
    /// terhitung dua kali.
    /// </summary>
    public static string? KindOf(string? viewName)
    {
        if (string.IsNullOrWhiteSpace(viewName))
        {
            return null;
        }

        var name = viewName.ToUpperInvariant();

        if (Matches(name, ReceptacleMarkers))
        {
            return DeviceKind.Receptacle;
        }

        return Matches(name, LightingMarkers) ? DeviceKind.Lighting : null;
    }

    private static bool Matches(string upperName, string[] markers)
    {
        foreach (var marker in markers)
        {
            if (upperName.Contains(marker, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
