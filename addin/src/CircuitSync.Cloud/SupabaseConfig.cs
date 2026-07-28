using System.Text.Json;

namespace CircuitSync.Cloud;

/// <summary>
/// URL project dan <c>anon</c> key. Ini dua-duanya memang publik: yang menjaga data
/// tetap terpisah adalah RLS, bukan kerahasiaan key. <c>service_role</c> key tidak
/// pernah menyentuh repo ini.
/// </summary>
public sealed record SupabaseConfig
{
    public const string EnvUrl = "CIRCUITSYNC_SUPABASE_URL";
    public const string EnvAnonKey = "CIRCUITSYNC_SUPABASE_ANON_KEY";
    public const string EnvWebUrl = "CIRCUITSYNC_WEB_URL";

    public required string Url { get; init; }

    public required string AnonKey { get; init; }

    /// <summary>Dipakai tombol "Buka web app".</summary>
    public string WebUrl { get; init; } = "https://circuitsync.vercel.app";

    public static SupabaseConfig Default { get; } = new()
    {
        Url = "https://fotpduvbevnncugruaom.supabase.co",
        AnonKey = "sb_publishable_caz_LIyAsoPmX4md3cI6aA_8tLbB4vo",
    };

    /// <summary>
    /// Urutan: environment variable, lalu <c>circuitsync.json</c> di samping DLL,
    /// lalu nilai bawaan. Urutan ini membuat satu build yang sama bisa dipakai
    /// ke project Supabase lain tanpa build ulang.
    /// </summary>
    public static SupabaseConfig Load()
    {
        var config = Default;

        var fromFile = ReadFile();
        if (fromFile is not null)
        {
            config = config with
            {
                Url = Clean(fromFile.Url) ?? config.Url,
                AnonKey = Clean(fromFile.AnonKey) ?? config.AnonKey,
                WebUrl = Clean(fromFile.WebUrl) ?? config.WebUrl,
            };
        }

        return config with
        {
            Url = Clean(Environment.GetEnvironmentVariable(EnvUrl)) ?? config.Url,
            AnonKey = Clean(Environment.GetEnvironmentVariable(EnvAnonKey)) ?? config.AnonKey,
            WebUrl = Clean(Environment.GetEnvironmentVariable(EnvWebUrl)) ?? config.WebUrl,
        };
    }

    private static FileShape? ReadFile()
    {
        try
        {
            var dir = Path.GetDirectoryName(typeof(SupabaseConfig).Assembly.Location);
            if (string.IsNullOrEmpty(dir))
            {
                return null;
            }

            var path = Path.Combine(dir, "circuitsync.json");
            if (!File.Exists(path))
            {
                return null;
            }

            return JsonSerializer.Deserialize<FileShape>(File.ReadAllText(path), Options);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Config rusak tidak boleh membuat add-in gagal dimuat; nilai bawaan tetap jalan.
            return null;
        }
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().TrimEnd('/');

    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    private sealed record FileShape(string? Url, string? AnonKey, string? WebUrl);
}
