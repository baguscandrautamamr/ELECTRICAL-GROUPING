using System.Windows.Media;

namespace CircuitSync.Ui;

public enum ThemeMode
{
    Light,
    Dark,
}

/// <summary>
/// Warna UI. Tidak ada hex mentah di luar file ini — sama seperti aturan token
/// Tailwind di sisi web, supaya kedua mode dijamin punya nilai untuk setiap peran.
/// </summary>
public sealed record Palette
{
    public required Color Background { get; init; }
    public required Color Surface { get; init; }
    public required Color SurfaceAlt { get; init; }
    public required Color Text { get; init; }
    public required Color TextMuted { get; init; }
    public required Color Border { get; init; }
    public required Color Accent { get; init; }
    public required Color AccentText { get; init; }
    public required Color Ok { get; init; }
    public required Color Warn { get; init; }
    public required Color Danger { get; init; }

    /// <summary>Terang adalah default.</summary>
    public static readonly Palette Light = new()
    {
        Background = Rgb(0xF5, 0xF5, 0xF7),
        Surface = Rgb(0xFF, 0xFF, 0xFF),
        SurfaceAlt = Rgb(0xF0, 0xF0, 0xF3),
        Text = Rgb(0x1D, 0x1D, 0x1F),
        TextMuted = Rgb(0x6E, 0x6E, 0x73),
        Border = Rgb(0xD8, 0xD8, 0xDE),
        Accent = Rgb(0x00, 0x71, 0xE3),
        AccentText = Rgb(0xFF, 0xFF, 0xFF),
        Ok = Rgb(0x1D, 0x8B, 0x4B),
        Warn = Rgb(0xA1, 0x6C, 0x00),
        Danger = Rgb(0xC0, 0x28, 0x2D),
    };

    public static readonly Palette Dark = new()
    {
        Background = Rgb(0x1C, 0x1C, 0x1E),
        Surface = Rgb(0x2C, 0x2C, 0x2E),
        SurfaceAlt = Rgb(0x3A, 0x3A, 0x3C),
        Text = Rgb(0xF5, 0xF5, 0xF7),
        TextMuted = Rgb(0x9E, 0x9E, 0xA6),
        Border = Rgb(0x48, 0x48, 0x4A),
        Accent = Rgb(0x0A, 0x84, 0xFF),
        AccentText = Rgb(0xFF, 0xFF, 0xFF),
        Ok = Rgb(0x30, 0xD1, 0x58),
        Warn = Rgb(0xFF, 0xD6, 0x0A),
        Danger = Rgb(0xFF, 0x45, 0x3A),
    };

    public static Palette For(ThemeMode mode) => mode == ThemeMode.Dark ? Dark : Light;

    private static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);
}
