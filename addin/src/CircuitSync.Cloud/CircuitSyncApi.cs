using System.Globalization;
using CircuitSync.Core;

namespace CircuitSync.Cloud;

/// <summary>
/// Operasi domain di atas <see cref="SupabaseClient"/>. Semua query PostgREST hidup
/// di sini, tidak berserakan di lapisan Revit atau UI.
/// </summary>
public sealed class CircuitSyncApi(SupabaseClient client)
{
    /// <summary>
    /// Batas baris per request. Angkanya konservatif: URL dan body PostgREST punya
    /// batas praktis, dan model besar bisa punya ribuan device.
    /// </summary>
    private const int BatchSize = 500;

    public SupabaseClient Client { get; } = client;

    /// <summary>
    /// Tabel yang ditolak database karena belum ada, dari migrasi yang belum ditembakkan.
    /// </summary>
    /// <remarks>
    /// Sepasang dengan <see cref="SupabaseClient.MissingColumns"/>. Dikumpulkan supaya UI
    /// bisa menyebutkannya: melewati sebuah tabel diam-diam membuat fitur yang
    /// bergantung padanya mati tanpa satu pun petunjuk kenapa.
    ///
    /// Isinya menggambarkan <b>tarikan terakhir</b>, bukan seluruh riwayat sesi — lihat
    /// pengosongannya di <see cref="PushSnapshotAsync"/>.
    /// </remarks>
    public IReadOnlyCollection<string> MissingTables => _missingTables;

    private readonly HashSet<string> _missingTables = new(StringComparer.Ordinal);

    // ---------------------------------------------------------------- project

    public Task<IReadOnlyList<ProjectRow>> ListProjectsAsync(CancellationToken ct = default) =>
        Client.SelectAsync<ProjectRow>("projects", "select=id,name,owner_id,updated_at&order=updated_at.desc", ct);

    public async Task<ProjectRow?> GetProjectAsync(Guid id, CancellationToken ct = default)
    {
        var rows = await Client.SelectAsync<ProjectRow>(
            "projects", $"select=id,name,owner_id,updated_at&id=eq.{id}&limit=1", ct).ConfigureAwait(false);
        return rows.Count > 0 ? rows[0] : null;
    }

    public async Task<ProjectRow> CreateProjectAsync(string name, CancellationToken ct = default)
    {
        var userId = Client.UserId ?? throw new CloudException("not_signed_in");
        var rows = await Client.InsertAsync<ProjectRow, object>(
            "projects", new { name = name.Trim(), owner_id = userId }, ct).ConfigureAwait(false);

        return rows.Count > 0 ? rows[0] : throw new CloudException("project_not_created");
    }

    public Task RenameProjectAsync(Guid id, string name, CancellationToken ct = default) =>
        Client.PatchAsync("projects", $"id=eq.{id}", new { name = name.Trim() }, ct);

    // ---------------------------------------------------------------- snapshot

    /// <summary>
    /// Mengirim seluruh isi model dan menyapu baris yang sudah tidak ada di model.
    /// Sapuan memakai <c>updated_at</c>: trigger di database menyentuhnya pada setiap
    /// upsert, jadi apa pun yang lebih tua dari stempel awal berarti tidak ikut terkirim.
    /// </summary>
    public async Task PushSnapshotAsync(Guid projectId, ModelSnapshot snapshot, CancellationToken ct = default)
    {
        var stamp = DateTimeOffset.UtcNow.AddSeconds(-1);

        // Catatan kolom dan tabel yang hilang dikosongkan dulu: peringatan di log harus
        // menggambarkan tarikan **ini**, bukan seluruh riwayat sesi Revit.
        //
        // Tanpa ini, sekali sebuah tabel tercatat hilang peringatannya terus muncul di
        // setiap tarikan berikutnya — termasuk setelah migrasinya diterapkan dan upsert-nya
        // sudah berhasil. Yang sampai ke user: "tabelnya belum ada" di sebelah "model
        // terkirim", dua baris yang saling membantah, dan yang benar justru yang kedua.
        _missingTables.Clear();
        Client.ForgetMissingColumns();

        // `levels`, `panels`, dan `devices` datang dari migrasi pertama: kalau ketiganya
        // tidak ada, database ini memang belum disiapkan, dan user berhak berhenti dengan
        // pesan — bukan mendapat tarikan model yang seolah berhasil padahal kosong.
        await UpsertBatchedAsync("levels",
            snapshot.Levels.Select(l => l with { ProjectId = projectId }).ToList(),
            "project_id,level_key", ct).ConfigureAwait(false);

        await UpsertBatchedAsync("panels",
            snapshot.Panels.Select(p => p with { ProjectId = projectId }).ToList(),
            "project_id,revit_unique_id", ct).ConfigureAwait(false);

        await UpsertBatchedAsync("devices",
            snapshot.Devices.Select(d => d with { ProjectId = projectId }).ToList(),
            "project_id,revit_unique_id", ct).ConfigureAwait(false);

        // Sisanya datang dari migrasi yang lebih baru, jadi masing-masing boleh belum ada.
        await UpsertOptionalAsync("layouts",
            snapshot.Layouts.Select(l => l with { ProjectId = projectId }).ToList(),
            "project_id,revit_unique_id", ct).ConfigureAwait(false);

        await UpsertOptionalAsync("lighting_devices",
            snapshot.LightingDevices.Select(d => d with { ProjectId = projectId }).ToList(),
            "project_id,revit_unique_id", ct).ConfigureAwait(false);

        await UpsertOptionalAsync("line_styles",
            snapshot.LineStyles.Select(s => s with { ProjectId = projectId }).ToList(),
            "project_id,revit_unique_id", ct).ConfigureAwait(false);

        // Setelah layouts dan devices: keduanya jadi tujuan foreign key baris ini.
        await UpsertOptionalAsync("layout_devices",
            snapshot.LayoutDevices.Select(m => m with { ProjectId = projectId }).ToList(),
            "project_id,layout_unique_id,device_unique_id", ct).ConfigureAwait(false);

        // Idem, dengan tujuan layouts dan lighting_devices.
        await UpsertOptionalAsync("layout_lighting_devices",
            snapshot.LayoutLightingDevices.Select(m => m with { ProjectId = projectId }).ToList(),
            "project_id,layout_unique_id,lighting_device_unique_id", ct).ConfigureAwait(false);

        // Sapuan keanggotaan ditaruh terakhir: menghapus layout, device, atau saklar lebih
        // dulu sudah membawa keanggotaannya lewat cascade, jadi yang tersisa di sini
        // hanya keanggotaan yang hilang sementara kedua ujungnya masih ada.
        //
        // Tabel yang tadi dilewati karena belum ada tidak perlu dicoba lagi di sini —
        // jawabannya sudah pasti sama.
        var cutoff = Uri.EscapeDataString(stamp.UtcDateTime.ToString("o", CultureInfo.InvariantCulture));
        foreach (var table in new[]
                 {
                     "levels", "layouts", "panels", "devices", "lighting_devices", "line_styles",
                     "layout_devices", "layout_lighting_devices",
                 })
        {
            if (_missingTables.Contains(table))
            {
                continue;
            }

            try
            {
                await Client.DeleteAsync(table, $"project_id=eq.{projectId}&updated_at=lt.{cutoff}", ct)
                    .ConfigureAwait(false);
            }
            catch (CloudException ex) when (PostgrestSchema.MissingTable(ex.Body) is { } missing)
            {
                _missingTables.Add(missing);
            }
        }

        await Client.InsertAsync<SyncJobRow, object>("sync_jobs", new
        {
            project_id = projectId,
            direction = SyncDirection.Snapshot,
            status = SyncJobStatus.Applied,
            applied_at = DateTimeOffset.UtcNow,
            payload = new
            {
                devices = snapshot.Devices.Count,
                panels = snapshot.Panels.Count,
                levels = snapshot.Levels.Count,
            },
        }, ct).ConfigureAwait(false);
    }

    // ---------------------------------------------------------------- jobs

    /// <summary>
    /// Job <c>apply</c> berstatus <c>queued</c> milik satu project, tertua dulu.
    /// </summary>
    public Task<IReadOnlyList<SyncJobRow>> FetchQueuedApplyJobsAsync(Guid projectId, CancellationToken ct = default) =>
        FetchQueuedJobsAsync(projectId, SyncDirection.Apply, ct);

    /// <summary>
    /// Job <c>wiring</c> berstatus <c>queued</c>: permintaan menggambar garis, bukan
    /// membuat circuit.
    /// </summary>
    public Task<IReadOnlyList<SyncJobRow>> FetchQueuedWiringJobsAsync(Guid projectId, CancellationToken ct = default) =>
        FetchQueuedJobsAsync(projectId, SyncDirection.Wiring, ct);

    private Task<IReadOnlyList<SyncJobRow>> FetchQueuedJobsAsync(Guid projectId, string direction,
        CancellationToken ct) =>
        Client.SelectAsync<SyncJobRow>("sync_jobs",
            $"select=id,project_id,direction,status,payload,error,applied_at" +
            $"&project_id=eq.{projectId}&direction=eq.{direction}" +
            $"&status=eq.{SyncJobStatus.Queued}&order=created_at.asc", ct);

    public async Task<IReadOnlyList<CircuitRow>> FetchCircuitsAsync(Guid projectId, IReadOnlyList<Guid> ids,
        CancellationToken ct = default)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var result = new List<CircuitRow>(ids.Count);
        foreach (var chunk in Chunk(ids, 100))
        {
            var list = string.Join(',', chunk);
            result.AddRange(await Client.SelectAsync<CircuitRow>("circuits",
                    "select=id,project_id,panel_unique_id,kind,device_unique_ids,circuit_number,status,revit_unique_id,error" +
                    $"&project_id=eq.{projectId}&id=in.({list})", ct)
                .ConfigureAwait(false));
        }

        return result;
    }

    // ---------------------------------------------------------------- wiring

    /// <summary>
    /// <c>UniqueId</c> garis wiring yang tercatat sedang ada di model untuk sebuah denah.
    /// </summary>
    /// <remarks>
    /// Kosong kalau tabelnya belum ada — dan itu ditangani sebagai "belum ada garis yang
    /// perlu dihapus", bukan sebagai kegagalan. Pengiriman pertama setelah migrasinya
    /// diterapkan memang tidak punya catatan apa pun.
    /// </remarks>
    public async Task<IReadOnlyList<string>> FetchWiringCurvesAsync(Guid projectId, string layoutUniqueId,
        CancellationToken ct = default)
    {
        try
        {
            var rows = await Client.SelectAsync<WiringCurveRow>("wiring_curves",
                    "select=project_id,layout_unique_id,revit_unique_id,switch_index" +
                    $"&project_id=eq.{projectId}&layout_unique_id=eq.{Uri.EscapeDataString(layoutUniqueId)}", ct)
                .ConfigureAwait(false);

            return rows.Select(row => row.RevitUniqueId).ToList();
        }
        catch (CloudException ex) when (PostgrestSchema.MissingTable(ex.Body) is { } missing)
        {
            _missingTables.Add(missing);
            return [];
        }
    }

    /// <summary>
    /// Mengganti catatan garis sebuah denah dengan yang baru saja digambar.
    /// </summary>
    /// <remarks>
    /// Hapus dulu lalu isi, bukan upsert: garis yang tadinya ada dan sekarang tidak lagi
    /// harus hilang dari catatan juga. Upsert hanya menambah, dan catatan yang menyebut
    /// garis yang sudah dihapus membuat pengiriman berikutnya mencari elemen hantu.
    ///
    /// Urutannya hapus-lalu-isi, dan itu memang ada celahnya: kalau pengisian gagal di
    /// tengah, catatannya jadi kurang dan garis yang tertinggal di model tidak akan
    /// terhapus pada pengiriman berikutnya. Yang dipilih di sini adalah kegagalan yang
    /// terlihat — garis dobel yang bisa dihapus user — di atas catatan yang menyebut
    /// elemen yang sudah tidak ada.
    /// </remarks>
    public async Task ReplaceWiringCurvesAsync(Guid projectId, string layoutUniqueId,
        IReadOnlyList<WiringCurveRow> curves, CancellationToken ct = default)
    {
        if (_missingTables.Contains("wiring_curves"))
        {
            return;
        }

        try
        {
            await Client.DeleteAsync("wiring_curves",
                    $"project_id=eq.{projectId}&layout_unique_id=eq.{Uri.EscapeDataString(layoutUniqueId)}", ct)
                .ConfigureAwait(false);

            await UpsertBatchedAsync("wiring_curves",
                curves.Select(c => c with { ProjectId = projectId }).ToList(),
                "project_id,revit_unique_id", ct).ConfigureAwait(false);
        }
        catch (CloudException ex) when (PostgrestSchema.MissingTable(ex.Body) is { } missing)
        {
            _missingTables.Add(missing);
        }
    }

    public Task MarkJobAppliedAsync(Guid jobId, CancellationToken ct = default) =>
        Client.PatchAsync("sync_jobs", $"id=eq.{jobId}", new
        {
            status = SyncJobStatus.Applied,
            applied_at = DateTimeOffset.UtcNow,
            error = (string?)null,
        }, ct);

    public Task MarkJobFailedAsync(Guid jobId, string error, CancellationToken ct = default) =>
        Client.PatchAsync("sync_jobs", $"id=eq.{jobId}", new
        {
            status = SyncJobStatus.Failed,
            applied_at = DateTimeOffset.UtcNow,
            error = Shorten(error),
        }, ct);

    // ---------------------------------------------------------------- write back

    /// <summary>
    /// Menulis balik hasil apply satu circuit, termasuk nomor yang dihasilkan Revit.
    /// </summary>
    public Task WriteCircuitResultAsync(Guid circuitId, string status, string? circuitNumber,
        string? revitUniqueId, string? error, CancellationToken ct = default) =>
        Client.PatchAsync("circuits", $"id=eq.{circuitId}", new
        {
            status,
            circuit_number = circuitNumber,
            revit_unique_id = revitUniqueId,
            error = error is null ? null : Shorten(error),
        }, ct);

    /// <summary>
    /// Memperbarui status, nomor circuit, dan panel device setelah apply, supaya denah
    /// di web berubah warna tanpa menunggu tarikan model berikutnya.
    /// </summary>
    /// <remarks>
    /// PATCH, bukan upsert. Upsert menulis <b>seluruh</b> kolom yang ada di body, dan
    /// <see cref="DeviceConnection"/> tidak membawa geometri — memakainya sebagai upsert
    /// akan mengosongkan <c>x_mm</c>, <c>y_mm</c>, dan <c>level_key</c> milik device yang
    /// baru saja di-circuit. PATCH hanya menyentuh kolom yang memang berubah.
    ///
    /// Device dikelompokkan per (status, nomor, panel) supaya satu circuit = satu
    /// request, bukan satu device = satu request. Panel ikut jadi kunci: tanpa itu dua
    /// circuit bernomor sama di panel berbeda — hal biasa, karena nomor hanya unik di
    /// dalam satu panel — akan digabung jadi satu PATCH dan salah satunya menulis panel
    /// yang keliru.
    /// </remarks>
    public async Task UpdateDeviceConnectionsAsync(Guid projectId, IReadOnlyList<DeviceConnection> devices,
        CancellationToken ct = default)
    {
        var groups = devices
            .GroupBy(d => (d.Status, d.CircuitNumber, d.PanelUniqueId))
            .Select(g => (g.Key.Status, g.Key.CircuitNumber, g.Key.PanelUniqueId,
                Ids: g.Select(d => d.RevitUniqueId).Distinct().ToList()));

        foreach (var (status, circuitNumber, panelUniqueId, ids) in groups)
        {
            foreach (var chunk in Chunk(ids, 100))
            {
                await Client.PatchAsync("devices",
                    $"project_id=eq.{projectId}&revit_unique_id=in.({InList(chunk)})",
                    new { status, circuit_number = circuitNumber, panel_unique_id = panelUniqueId },
                    ct).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Daftar untuk operator <c>in.()</c> PostgREST. Tiap nilai dikutip supaya karakter
    /// apa pun di dalam UniqueId tidak dibaca sebagai pemisah.
    /// </summary>
    private static string InList(IEnumerable<string> values) =>
        string.Join(',', values.Select(v => Uri.EscapeDataString($"\"{v.Replace("\"", "\\\"")}\"")));

    // ---------------------------------------------------------------- helpers

    private async Task UpsertBatchedAsync<T>(string table, IReadOnlyList<T> rows, string onConflict,
        CancellationToken ct)
    {
        foreach (var chunk in Chunk(rows, BatchSize))
        {
            await Client.UpsertAsync(table, chunk, onConflict, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Upsert ke tabel yang boleh saja belum ada di database.
    /// </summary>
    /// <remarks>
    /// Add-in dipasang user lewat ZIP; migrasi ditembakkan lewat <c>supabase db push</c>.
    /// Selisih di antara keduanya bukan kemungkinan melainkan keadaan biasa, dan setiap
    /// fitur baru datang bersama tabelnya sendiri. Tanpa toleransi ini satu tabel yang
    /// belum ada menggagalkan seluruh tarikan model — device dan panel yang sudah lama
    /// ada pun tidak terkirim, dan yang sampai ke user hanya <c>http_404</c>.
    ///
    /// Yang dilewati dicatat, bukan didiamkan, supaya UI bisa menyebut tabelnya beserta
    /// jalan keluarnya. Begitu migrasinya diterapkan, tarikan berikutnya mengisinya
    /// sendiri — tanpa memasang ulang add-in.
    /// </remarks>
    private async Task UpsertOptionalAsync<T>(string table, IReadOnlyList<T> rows, string onConflict,
        CancellationToken ct)
    {
        try
        {
            await UpsertBatchedAsync(table, rows, onConflict, ct).ConfigureAwait(false);
        }
        catch (CloudException ex) when (PostgrestSchema.MissingTable(ex.Body) is { } missing)
        {
            _missingTables.Add(missing);
        }
    }

    private static IEnumerable<List<T>> Chunk<T>(IReadOnlyList<T> source, int size)
    {
        for (var start = 0; start < source.Count; start += size)
        {
            yield return source.Skip(start).Take(size).ToList();
        }
    }

    private static string Shorten(string text) => text.Length <= 400 ? text : text[..400];
}
