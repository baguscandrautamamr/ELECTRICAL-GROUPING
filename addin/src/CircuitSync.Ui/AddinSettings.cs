using System.IO;
using System.Text.Json;
using CircuitSync.Core;

namespace CircuitSync.Ui;

/// <summary>
/// Pilihan bahasa dan tema. Bukan rahasia, jadi JSON polos — beda dari refresh token
/// yang lewat DPAPI.
/// </summary>
public sealed class AddinSettings
{
    private static readonly string Path = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CircuitSync",
        "settings.json");

    public Language Language { get; set; } = Language.Id;

    /// <summary>Terang adalah default, gelap wajib bekerja.</summary>
    public ThemeMode Theme { get; set; } = ThemeMode.Light;

    public int PollSeconds { get; set; } = 20;

    public bool AutoPoll { get; set; }

    public bool PlaceTags { get; set; } = true;

    /// <summary>
    /// Alamat web app, kalau user menimpanya dari panel. Kosong berarti pakai
    /// <see cref="SupabaseConfig.WebUrl"/>.
    /// </summary>
    /// <remarks>
    /// Nama domain <c>*.vercel.app</c> mengikuti nama project di Vercel, jadi nilai
    /// bawaan yang dikompilasi ke DLL tidak selalu cocok dengan deployment yang
    /// sebenarnya. Bisa diubah dari panel supaya tidak perlu build ulang add-in
    /// hanya untuk memperbaiki satu tautan.
    /// </remarks>
    public string? WebUrl { get; set; }

    public static AddinSettings Load()
    {
        try
        {
            if (File.Exists(Path))
            {
                return JsonSerializer.Deserialize<Shape>(File.ReadAllText(Path)) is { } shape
                    ? new AddinSettings
                    {
                        Language = Translator.ParseLanguage(shape.Language),
                        Theme = string.Equals(shape.Theme, "dark", StringComparison.OrdinalIgnoreCase)
                            ? ThemeMode.Dark
                            : ThemeMode.Light,
                        PollSeconds = shape.PollSeconds is >= 5 and <= 600 ? shape.PollSeconds : 20,
                        AutoPoll = shape.AutoPoll,
                        PlaceTags = shape.PlaceTags,
                        WebUrl = Trimmed(shape.WebUrl),
                    }
                    : new AddinSettings();
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
        }

        return new AddinSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            var shape = new Shape(
                Translator.ToCode(Language),
                Theme == ThemeMode.Dark ? "dark" : "light",
                PollSeconds,
                AutoPoll,
                PlaceTags,
                WebUrl);
            File.WriteAllText(Path, JsonSerializer.Serialize(shape, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record Shape(
        string Language, string Theme, int PollSeconds, bool AutoPoll, bool PlaceTags, string? WebUrl);
}
