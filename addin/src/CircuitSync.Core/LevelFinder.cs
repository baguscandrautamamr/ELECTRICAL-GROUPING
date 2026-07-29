namespace CircuitSync.Core;

/// <summary>
/// Menebak level sebuah device dari ketinggiannya, saat Revit tidak memberitahu
/// levelnya secara langsung.
/// </summary>
/// <remarks>
/// Device yang diletakkan di lantai punya <c>LevelId</c> sendiri. Yang di-host di
/// dinding atau ceiling sering tidak: family berbasis face menyimpan levelnya di
/// tempat lain, dan kadang tidak menyimpannya sama sekali. Dulu semua kasus itu
/// jatuh ke <c>unassigned</c>, dan karena halaman denah menyaring per level, device
/// tersebut hilang dari web tanpa jejak — terdeteksi di model, tidak pernah muncul
/// di layar.
///
/// Hidup di Core, bukan di lapisan Revit, karena ini keputusan yang bisa salah dan
/// karena itu harus bisa dites: lapisan Revit cukup menyerahkan ketinggian device
/// beserta daftar level.
/// </remarks>
public static class LevelFinder
{
    /// <summary>
    /// Toleransi ke atas, dalam milimeter. Device yang sedikit di bawah pelat —
    /// misalnya stop kontak lantai yang tertanam — tetap milik level di atasnya,
    /// bukan level di bawahnya.
    /// </summary>
    public const double ToleranceMm = 300;

    /// <summary>
    /// <c>level_key</c> yang paling masuk akal untuk sebuah titik, atau null kalau
    /// tidak ada level sama sekali.
    /// </summary>
    /// <remarks>
    /// Aturannya: level tertinggi yang masih berada di bawah titik itu. Device
    /// menempel pada lantai tempat ia berdiri, dan dinding setinggi tiga meter tetap
    /// milik level di kakinya — bukan milik level di atas kepalanya, yang akan dipilih
    /// kalau kita sekadar mencari elevasi terdekat.
    /// </remarks>
    public static string? KeyFor(IReadOnlyList<LevelRow> levels, double zMm)
    {
        if (levels.Count == 0)
        {
            return null;
        }

        LevelRow? below = null;
        LevelRow? lowest = null;

        foreach (var level in levels)
        {
            if (lowest is null || level.ElevationMm < lowest.ElevationMm)
            {
                lowest = level;
            }

            if (level.ElevationMm <= zMm + ToleranceMm &&
                (below is null || level.ElevationMm > below.ElevationMm))
            {
                below = level;
            }
        }

        // Titik di bawah level terendah — basement yang levelnya tidak dimodelkan,
        // atau koordinat yang meleset. Level terendah lebih berguna daripada menyerah.
        return (below ?? lowest)?.LevelKey;
    }
}
