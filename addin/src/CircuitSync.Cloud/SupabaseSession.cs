using System.Text.Json.Serialization;

namespace CircuitSync.Cloud;

public sealed record SupabaseSession
{
    [JsonPropertyName("access_token")] public string AccessToken { get; init; } = "";
    [JsonPropertyName("refresh_token")] public string RefreshToken { get; init; } = "";
    [JsonPropertyName("expires_in")] public int ExpiresIn { get; init; }
    [JsonPropertyName("user")] public SupabaseUser? User { get; init; }

    /// <summary>Diisi lokal saat token diterima; server tidak mengirim ini.</summary>
    [JsonPropertyName("expires_at_utc")] public DateTimeOffset ExpiresAtUtc { get; init; }

    /// <summary>
    /// Dianggap kedaluwarsa satu menit lebih awal, supaya request yang sedang
    /// terbang tidak kehabisan token di tengah jalan.
    /// </summary>
    public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresAtUtc - TimeSpan.FromMinutes(1);

    public string? Email => User?.Email;

    public SupabaseSession Stamped() => this with
    {
        ExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(ExpiresIn > 0 ? ExpiresIn : 3600),
    };
}

public sealed record SupabaseUser
{
    [JsonPropertyName("id")] public Guid Id { get; init; }
    [JsonPropertyName("email")] public string? Email { get; init; }
}
