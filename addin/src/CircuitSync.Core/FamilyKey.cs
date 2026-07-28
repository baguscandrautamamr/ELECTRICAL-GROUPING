namespace CircuitSync.Core;

/// <summary>
/// <c>family_key</c> = <c>"{FamilyName}::{TypeName}"</c>. Kunci mapping simbol 2D di web,
/// jadi bentuknya harus stabil dan dihasilkan hanya dari sini.
/// </summary>
public static class FamilyKey
{
    public const string Separator = "::";

    public static string Make(string? familyName, string? typeName)
    {
        var family = Clean(familyName);
        var type = Clean(typeName);
        return family + Separator + type;
    }

    public static bool TryParse(string? key, out string familyName, out string typeName)
    {
        familyName = "";
        typeName = "";

        if (string.IsNullOrEmpty(key))
        {
            return false;
        }

        var at = key.IndexOf(Separator, StringComparison.Ordinal);
        if (at < 0)
        {
            return false;
        }

        familyName = key[..at];
        typeName = key[(at + Separator.Length)..];
        return true;
    }

    /// <summary>
    /// Nama family/type di Revit boleh mengandung apa saja termasuk titik dua.
    /// Separator diganti spasi supaya key tidak pernah punya lebih dari satu pemisah.
    /// </summary>
    private static string Clean(string? value)
    {
        var text = (value ?? "").Trim();
        return text.Replace(Separator, " ", StringComparison.Ordinal);
    }
}
