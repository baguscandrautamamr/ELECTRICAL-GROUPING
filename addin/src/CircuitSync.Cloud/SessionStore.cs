using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CircuitSync.Cloud;

/// <summary>
/// Menyimpan refresh token antar sesi Revit. Isi file dienkripsi DPAPI dengan scope
/// <see cref="DataProtectionScope.CurrentUser"/>, jadi file yang dicuri tidak berguna
/// di akun Windows lain.
/// </summary>
public sealed class SessionStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("CircuitSync/session/v1");

    private readonly string _path;

    public SessionStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CircuitSync",
            "session.bin");
    }

    /// <summary>
    /// Hanya refresh token yang disimpan. Access token berumur satu jam — menyimpannya
    /// tidak menghemat apa pun dan hanya memperbesar yang bisa dicuri.
    /// </summary>
    public void Save(SupabaseSession session)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrEmpty(session.RefreshToken))
        {
            return;
        }

        try
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(
                new Stored(session.RefreshToken, session.Email));
            var sealed_ = ProtectedData.Protect(payload, Entropy, DataProtectionScope.CurrentUser);

            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllBytes(_path, sealed_);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            // Gagal menyimpan berarti user harus masuk lagi nanti. Bukan alasan
            // untuk menggagalkan login yang sedang berhasil.
        }
    }

    public (string RefreshToken, string? Email)? Load()
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(_path))
        {
            return null;
        }

        try
        {
            var opened = ProtectedData.Unprotect(File.ReadAllBytes(_path), Entropy, DataProtectionScope.CurrentUser);
            var stored = JsonSerializer.Deserialize<Stored>(opened);
            return string.IsNullOrEmpty(stored?.RefreshToken) ? null : (stored.RefreshToken, stored.Email);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                      or CryptographicException or JsonException)
        {
            Clear();
            return null;
        }
    }

    public void Clear()
    {
        try
        {
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private sealed record Stored(string RefreshToken, string? Email);
}
