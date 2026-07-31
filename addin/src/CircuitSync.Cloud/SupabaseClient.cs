using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CircuitSync.Core;

namespace CircuitSync.Cloud;

/// <summary>
/// Dilempar untuk semua kegagalan yang berasal dari cloud. UI menerjemahkannya jadi
/// satu baris log, tidak menampilkan isi <see cref="Exception.Message"/> mentah.
/// </summary>
public sealed class CloudException : Exception
{
    public CloudException(string message, HttpStatusCode? status = null, string? body = null)
        : base(message)
    {
        Status = status;
        Body = body;
    }

    public HttpStatusCode? Status { get; }

    public string? Body { get; }

    public bool IsAuthFailure => Status is HttpStatusCode.Unauthorized or HttpStatusCode.BadRequest;

    /// <summary>
    /// Baris untuk log aktivitas. Kode HTTP sendirian tidak menolong siapa pun:
    /// "http_400" bisa berarti kolom tidak dikenal, constraint ditolak, atau payload
    /// salah bentuk. PostgREST menaruh sebab sebenarnya di body sebagai JSON,
    /// jadi itu yang ikut ditampilkan.
    /// </summary>
    public string Describe()
    {
        var body = Body;
        if (string.IsNullOrWhiteSpace(body))
        {
            return Message;
        }

        return $"{Message} — {ServerMessage(body) ?? body}";
    }

    private static string? ServerMessage(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            // PostgREST: {"code":"PGRST102","message":"All object keys must match",...}
            // GoTrue memakai "error_description" atau "msg" untuk hal yang sama.
            foreach (var name in new[] { "message", "error_description", "msg", "error" })
            {
                if (document.RootElement.TryGetProperty(name, out var value) &&
                    value.ValueKind == JsonValueKind.String)
                {
                    return value.GetString();
                }
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>
/// Satu HttpClient untuk auth GoTrue dan PostgREST. Tidak ada SDK Supabase di sini:
/// Revit memuat semua add-in ke satu AppDomain, jadi setiap dependensi tambahan adalah
/// risiko bentrok versi dengan add-in lain.
/// </summary>
public sealed class SupabaseClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly SessionStore _store;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    private SupabaseSession? _session;

    public SupabaseClient(SupabaseConfig? config = null, HttpMessageHandler? handler = null, SessionStore? store = null)
    {
        Config = config ?? SupabaseConfig.Load();
        _store = store ?? new SessionStore();
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.BaseAddress = new Uri(Config.Url + "/");
        _http.Timeout = TimeSpan.FromSeconds(30);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public SupabaseConfig Config { get; }

    public bool IsSignedIn => _session is not null;

    public string? UserEmail => _session?.Email;

    public Guid? UserId => _session?.User?.Id;

    /// <summary>
    /// Mencoba memulihkan sesi dari refresh token tersimpan. Dipanggil sekali saat
    /// panel dibuka; gagal berarti user cukup masuk lagi, bukan error.
    /// </summary>
    public async Task<bool> TryRestoreAsync(CancellationToken ct = default)
    {
        var stored = _store.Load();
        if (stored is null)
        {
            return false;
        }

        try
        {
            _session = await RequestTokenAsync("refresh_token",
                new { refresh_token = stored.Value.RefreshToken }, ct).ConfigureAwait(false);
            _store.Save(_session);
            return true;
        }
        catch (CloudException)
        {
            _store.Clear();
            return false;
        }
    }

    public async Task SignInWithPasswordAsync(string email, string password, CancellationToken ct = default)
    {
        _session = await RequestTokenAsync("password", new { email, password }, ct).ConfigureAwait(false);
        _store.Save(_session);
    }

    /// <summary>Mengirim kode enam angka ke email. Tidak membuat user baru.</summary>
    public async Task SendEmailCodeAsync(string email, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "auth/v1/otp")
        {
            Content = JsonContent.Create(new { email, create_user = false }),
        };
        request.Headers.TryAddWithoutValidation("apikey", Config.AnonKey);

        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        await EnsureOkAsync(response, ct).ConfigureAwait(false);
    }

    public async Task VerifyEmailCodeAsync(string email, string code, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "auth/v1/verify")
        {
            Content = JsonContent.Create(new { email, token = code.Trim(), type = "email" }),
        };
        request.Headers.TryAddWithoutValidation("apikey", Config.AnonKey);

        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        await EnsureOkAsync(response, ct).ConfigureAwait(false);

        _session = (await ReadAsync<SupabaseSession>(response, ct).ConfigureAwait(false)).Stamped();
        _store.Save(_session);
    }

    public async Task SignOutAsync(CancellationToken ct = default)
    {
        var token = _session?.AccessToken;
        _session = null;
        _store.Clear();

        if (string.IsNullOrEmpty(token))
        {
            return;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "auth/v1/logout");
            request.Headers.TryAddWithoutValidation("apikey", Config.AnonKey);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Sesi lokal sudah dibuang; gagal memberi tahu server tidak mengubah itu.
        }
    }

    // ---------------------------------------------------------------- PostgREST

    public async Task<IReadOnlyList<T>> SelectAsync<T>(string table, string query, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"rest/v1/{table}?{query}");
        using var response = await SendAuthorizedAsync(request, ct).ConfigureAwait(false);
        await EnsureOkAsync(response, ct).ConfigureAwait(false);
        return await ReadAsync<List<T>>(response, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Upsert. <paramref name="onConflict"/> menyebut kolom kunci, dan header
    /// <c>Prefer: resolution=merge-duplicates</c> yang membuatnya jadi upsert.
    /// </summary>
    public async Task UpsertAsync<T>(string table, IReadOnlyList<T> rows, string onConflict,
        CancellationToken ct = default)
    {
        if (rows.Count == 0)
        {
            return;
        }

        var path = $"rest/v1/{table}?on_conflict={Uri.EscapeDataString(onConflict)}";
        using var response = await WriteAsync(HttpMethod.Post, path,
            "resolution=merge-duplicates,return=minimal", rows, table, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<T>> InsertAsync<T, TBody>(string table, TBody row, CancellationToken ct = default)
    {
        using var response = await WriteAsync(HttpMethod.Post, $"rest/v1/{table}",
            "return=representation", row, table, ct).ConfigureAwait(false);
        return await ReadAsync<List<T>>(response, ct).ConfigureAwait(false);
    }

    public async Task PatchAsync<TBody>(string table, string query, TBody patch, CancellationToken ct = default)
    {
        using var response = await WriteAsync(HttpMethod.Patch, $"rest/v1/{table}?{query}",
            "return=minimal", patch, table, ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string table, string query, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"rest/v1/{table}?{query}");
        request.Headers.TryAddWithoutValidation("Prefer", "return=minimal");

        using var response = await SendAuthorizedAsync(request, ct).ConfigureAwait(false);
        await EnsureOkAsync(response, ct).ConfigureAwait(false);
    }

    // ---------------------------------------------------------------- internals

    /// <summary>Batas berapa kolom yang boleh dibuang sebelum menyerah.</summary>
    /// <remarks>
    /// Ada batasnya supaya database yang benar-benar salah bentuk — tabel dari project
    /// lain, misalnya — berhenti sebagai kegagalan, bukan terkelupas kolom demi kolom
    /// sampai yang terkirim tinggal kunci primernya.
    /// </remarks>
    private const int MaxDroppedColumns = 4;

    /// <summary>
    /// Kolom yang ditolak database karena belum dikenal, per tabel: <c>"devices.panel_unique_id"</c>.
    /// </summary>
    /// <remarks>
    /// Dikumpulkan supaya UI bisa menyebutkannya. Membuang kolom diam-diam akan membuat
    /// fitur yang bergantung padanya tidak jalan tanpa satu pun petunjuk kenapa —
    /// tepat jenis kegagalan yang paling lama tidak ketahuan.
    /// </remarks>
    public IReadOnlyCollection<string> MissingColumns => _missingColumns;

    private readonly HashSet<string> _missingColumns = new(StringComparer.Ordinal);

    /// <summary>
    /// Permintaan bertubuh JSON yang tahan terhadap database yang tertinggal satu migrasi.
    /// </summary>
    /// <remarks>
    /// Add-in dan database dipasang terpisah: ZIP add-in dipasang user, migrasi
    /// ditembakkan lewat <c>supabase db push</c>. Selisih versi di antara keduanya bukan
    /// kemungkinan, melainkan keadaan biasa — dan sebelum ini selisih itu menggagalkan
    /// <b>seluruh</b> tarikan model dengan pesan PostgREST mentah, bukan hanya fitur yang
    /// memang butuh kolom baru.
    ///
    /// Jadi kolom yang ditolak dibuang dari body lalu permintaannya diulang. Sisanya tetap
    /// masuk. Begitu migrasinya diterapkan, payload penuh kembali terkirim dengan
    /// sendirinya — tanpa memasang ulang add-in.
    /// </remarks>
    private async Task<HttpResponseMessage> WriteAsync<TBody>(HttpMethod method, string path, string prefer,
        TBody payload, string table, CancellationToken ct)
    {
        var body = JsonSerializer.SerializeToNode(payload, CircuitSyncJson.Options);
        var dropped = 0;

        while (true)
        {
            using var request = new HttpRequestMessage(method, path)
            {
                Content = new StringContent(body?.ToJsonString() ?? "null", Encoding.UTF8, "application/json"),
            };
            request.Headers.TryAddWithoutValidation("Prefer", prefer);

            var response = await SendAuthorizedAsync(request, ct).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                return response;
            }

            CloudException failure;
            using (response)
            {
                var raw = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                failure = new CloudException($"http_{(int)response.StatusCode}", response.StatusCode, Trim(raw));
            }

            var column = PostgrestSchema.UnknownColumn(failure.Body);
            if (column is null || dropped >= MaxDroppedColumns || !PostgrestSchema.Strip(body, column))
            {
                throw failure;
            }

            _missingColumns.Add($"{table}.{column}");
            dropped++;
        }
    }

    private async Task<HttpResponseMessage> SendAuthorizedAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var token = await ValidAccessTokenAsync(ct).ConfigureAwait(false);

        request.Headers.TryAddWithoutValidation("apikey", Config.AnonKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        try
        {
            return await _http.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new CloudException("network", null, ex.Message);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new CloudException("timeout");
        }
    }

    private async Task<string> ValidAccessTokenAsync(CancellationToken ct)
    {
        var current = _session ?? throw new CloudException("not_signed_in", HttpStatusCode.Unauthorized);
        if (!current.IsExpired)
        {
            return current.AccessToken;
        }

        await _refreshLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Bisa saja panggilan lain sudah menyegarkan sesi sambil kita menunggu kunci.
            if (_session is { IsExpired: false } fresh)
            {
                return fresh.AccessToken;
            }

            var refreshToken = _session?.RefreshToken;
            if (string.IsNullOrEmpty(refreshToken))
            {
                throw new CloudException("not_signed_in", HttpStatusCode.Unauthorized);
            }

            _session = await RequestTokenAsync("refresh_token", new { refresh_token = refreshToken }, ct)
                .ConfigureAwait(false);
            _store.Save(_session);
            return _session.AccessToken;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task<SupabaseSession> RequestTokenAsync(string grantType, object body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"auth/v1/token?grant_type={grantType}")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.TryAddWithoutValidation("apikey", Config.AnonKey);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new CloudException("network", null, ex.Message);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new CloudException("timeout");
        }

        using (response)
        {
            await EnsureOkAsync(response, ct).ConfigureAwait(false);
            var session = await ReadAsync<SupabaseSession>(response, ct).ConfigureAwait(false);
            if (string.IsNullOrEmpty(session.AccessToken))
            {
                throw new CloudException("empty_session", response.StatusCode);
            }

            return session.Stamped();
        }
    }

    private static async Task EnsureOkAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        throw new CloudException($"http_{(int)response.StatusCode}", response.StatusCode, Trim(body));
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        try
        {
            return JsonSerializer.Deserialize<T>(json, CircuitSyncJson.Options)
                   ?? throw new CloudException("empty_body", response.StatusCode);
        }
        catch (JsonException ex)
        {
            throw new CloudException("bad_json", response.StatusCode, ex.Message);
        }
    }

    private static string Trim(string body) => body.Length <= 500 ? body : body[..500];

    public void Dispose()
    {
        _http.Dispose();
        _refreshLock.Dispose();
    }
}
