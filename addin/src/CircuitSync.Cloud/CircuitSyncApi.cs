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

        await UpsertBatchedAsync("levels",
            snapshot.Levels.Select(l => l with { ProjectId = projectId }).ToList(),
            "project_id,level_key", ct).ConfigureAwait(false);

        await UpsertBatchedAsync("layouts",
            snapshot.Layouts.Select(l => l with { ProjectId = projectId }).ToList(),
            "project_id,revit_unique_id", ct).ConfigureAwait(false);

        await UpsertBatchedAsync("panels",
            snapshot.Panels.Select(p => p with { ProjectId = projectId }).ToList(),
            "project_id,revit_unique_id", ct).ConfigureAwait(false);

        await UpsertBatchedAsync("devices",
            snapshot.Devices.Select(d => d with { ProjectId = projectId }).ToList(),
            "project_id,revit_unique_id", ct).ConfigureAwait(false);

        var cutoff = Uri.EscapeDataString(stamp.UtcDateTime.ToString("o", CultureInfo.InvariantCulture));
        foreach (var table in new[] { "levels", "layouts", "panels", "devices" })
        {
            await Client.DeleteAsync(table, $"project_id=eq.{projectId}&updated_at=lt.{cutoff}", ct)
                .ConfigureAwait(false);
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
        Client.SelectAsync<SyncJobRow>("sync_jobs",
            $"select=id,project_id,direction,status,payload,error,applied_at" +
            $"&project_id=eq.{projectId}&direction=eq.{SyncDirection.Apply}" +
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
    /// Memperbarui status dan nomor circuit device setelah apply, supaya denah di web
    /// berubah warna tanpa menunggu tarikan model berikutnya.
    /// </summary>
    /// <remarks>
    /// PATCH, bukan upsert. Upsert menulis <b>seluruh</b> kolom yang ada di body, dan
    /// <see cref="DeviceConnection"/> tidak membawa geometri — memakainya sebagai upsert
    /// akan mengosongkan <c>x_mm</c>, <c>y_mm</c>, dan <c>level_key</c> milik device yang
    /// baru saja di-circuit. PATCH hanya menyentuh dua kolom yang memang berubah.
    ///
    /// Device dikelompokkan per (status, nomor) supaya satu circuit = satu request,
    /// bukan satu device = satu request.
    /// </remarks>
    public async Task UpdateDeviceConnectionsAsync(Guid projectId, IReadOnlyList<DeviceConnection> devices,
        CancellationToken ct = default)
    {
        var groups = devices
            .GroupBy(d => (d.Status, d.CircuitNumber))
            .Select(g => (g.Key.Status, g.Key.CircuitNumber, Ids: g.Select(d => d.RevitUniqueId).Distinct().ToList()));

        foreach (var (status, circuitNumber, ids) in groups)
        {
            foreach (var chunk in Chunk(ids, 100))
            {
                await Client.PatchAsync("devices",
                    $"project_id=eq.{projectId}&revit_unique_id=in.({InList(chunk)})",
                    new { status, circuit_number = circuitNumber }, ct).ConfigureAwait(false);
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

    private static IEnumerable<List<T>> Chunk<T>(IReadOnlyList<T> source, int size)
    {
        for (var start = 0; start < source.Count; start += size)
        {
            yield return source.Skip(start).Take(size).ToList();
        }
    }

    private static string Shorten(string text) => text.Length <= 400 ? text : text[..400];
}
