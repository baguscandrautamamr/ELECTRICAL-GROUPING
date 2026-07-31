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
    /// Nama tabel yang belum ada di database, dari body error PostgREST — atau null kalau
    /// kegagalannya sebab lain.
    /// </summary>
    /// <remarks>
    /// Sepasang dengan <see cref="UnknownColumn"/>, dan ada karena alasan yang sama:
    /// add-in dipasang user lewat ZIP, migrasi ditembakkan lewat <c>supabase db push</c>,
    /// dan selisih di antara keduanya adalah keadaan biasa. Yang tertangani sebelumnya cuma
    /// kolom yang belum ada. Tabel yang belum ada — kasus yang justru lebih sering, karena
    /// setiap fitur baru datang bersama tabelnya — menggagalkan <b>seluruh</b> tarikan
    /// model dengan <c>http_404</c>, termasuk device dan panel yang sudah lama ada.
    ///
    /// PostgREST menjawabnya <c>PGRST205</c> dengan pesan seperti
    /// <c>Could not find the table 'public.line_styles' in the schema cache</c>. Postgres
    /// sendiri memakai <c>42P01</c> untuk hal yang sama pada fungsi; keduanya diterima di
    /// sini, sama seperti yang sudah dilakukan <c>web/lib/supabase/errors.ts</c>.
    ///
    /// Kodenya diperiksa lebih dulu, bukan hanya pola pesannya — melewatkan sebuah tabel
    /// karena salah baca berarti fitur mati diam-diam.
    /// </remarks>
    public static string? MissingTable(string? body)
    {
        if (string.IsNullOrEmpty(body) ||
            (!body.Contains("PGRST205", StringComparison.Ordinal) &&
             !body.Contains("42P01", StringComparison.Ordinal)))
        {
            return null;
        }

        // Pesannya dibaca dari JSON-nya, bukan dari teks mentah body. PostgREST menulis
        // nama tabel di antara petik tunggal, tapi Postgres memakai petik ganda — dan di
        // dalam body JSON petik ganda itu ter-escape jadi \", sehingga regex terhadap teks
        // mentah meleset persis pada bentuk yang datang dari fungsi database.
        var message = Message(body) ?? body;

        var match = Regex.Match(message, @"Could not find the table '(?:public\.)?([^']+)'", RegexOptions.None,
            TimeSpan.FromSeconds(1));
        if (match.Success)
        {
            return match.Groups[1].Value;
        }

        // 42P01 dari Postgres berbunyi lain: relation "public.line_styles" does not exist.
        match = Regex.Match(message, @"relation ""(?:public\.)?([^""]+)"" does not exist", RegexOptions.None,
            TimeSpan.FromSeconds(1));
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>
    /// Isi field <c>message</c> dari body error, atau null kalau body-nya bukan JSON objek
    /// berisi pesan.
    /// </summary>
    private static string? Message(string body)
    {
        try
        {
            if (JsonNode.Parse(body) is JsonObject root &&
                root["message"] is JsonValue value &&
                value.TryGetValue<string>(out var text))
            {
                return text;
            }
        }
        catch (JsonException)
        {
            // Body yang bukan JSON tetap dicoba apa adanya oleh pemanggilnya.
        }

        return null;
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
