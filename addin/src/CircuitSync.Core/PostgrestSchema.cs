using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace CircuitSync.Core;

/// <summary>
/// Membaca keluhan PostgREST tentang kolom yang belum dikenal, dan membuang kolom itu
/// dari payload.
/// </summary>
/// <remarks>
/// Ada di Core, bukan di Cloud, karena isinya tidak menyentuh HTTP sama sekali —
/// sebuah string dan sebuah pohon JSON masuk, keputusan keluar. Itu yang membuatnya
/// bisa dites di runner Linux, dan perilaku ini termasuk yang paling perlu dites:
/// ia hanya berjalan saat add-in lebih baru daripada database, keadaan yang justru
/// jarang terjadi di mesin orang yang menulis kodenya.
/// </remarks>
public static class PostgrestSchema
{
    /// <summary>
    /// Nama kolom yang belum dikenal database, dari body error PostgREST — atau null
    /// kalau kegagalannya sebab lain.
    /// </summary>
    /// <remarks>
    /// PostgREST menjawab <c>PGRST204</c> dengan pesan seperti
    /// <c>Could not find the 'panel_unique_id' column of 'devices' in the schema cache</c>.
    ///
    /// Kodenya diperiksa lebih dulu, bukan hanya pola pesannya: kegagalan lain bisa saja
    /// memuat tanda petik, dan membuang kolom karena salah baca jauh lebih buruk daripada
    /// menyerah — yang satu menghilangkan data diam-diam, yang lain berhenti dengan pesan.
    /// </remarks>
    public static string? UnknownColumn(string? body)
    {
        if (string.IsNullOrEmpty(body) || !body.Contains("PGRST204", StringComparison.Ordinal))
        {
            return null;
        }

        var match = Regex.Match(body, @"Could not find the '([^']+)' column", RegexOptions.None,
            TimeSpan.FromSeconds(1));
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>
    /// Membuang satu kolom dari payload, baik ia satu objek maupun array baris.
    /// </summary>
    /// <returns>
    /// False kalau kolomnya memang tidak ada di sana — tanda pesannya bicara tentang hal
    /// lain, dan mengulang permintaan yang sama persis tidak akan mengubah hasilnya.
    /// </returns>
    public static bool Strip(JsonNode? payload, string column) => payload switch
    {
        JsonObject row => row.Remove(column),
        // Sengaja tidak short-circuit: kolomnya harus hilang dari **semua** baris.
        // PostgREST menolak bulk insert yang objeknya tidak sekunci dengan
        // "All object keys must match", jadi menyisakannya di satu baris saja
        // menukar satu kegagalan dengan kegagalan lain yang lebih membingungkan.
        JsonArray rows => rows.OfType<JsonObject>().Count(row => row.Remove(column)) > 0,
        _ => false,
    };
}
