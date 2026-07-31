using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CircuitSync.Cloud;
using CircuitSync.Core;
using CircuitSync.Revit;

namespace CircuitSync.Ui;

public enum LogKind
{
    Info,
    Ok,
    Warn,
    Error,
}

/// <summary>
/// Satu baris aktivitas. Menyimpan kunci terjemahan, bukan teks jadi, supaya log lama
/// ikut berubah saat bahasa diganti.
/// </summary>
public sealed record LogEntry(DateTime At, LogKind Kind, string Key, object?[] Args);

/// <summary>
/// Perekat antara Revit, cloud, dan window. Tidak menyentuh WPF sama sekali —
/// window yang bertugas memindahkan event ke dispatcher.
///
/// Aturan thread yang dipegang di sini: baca/tulis model hanya di dalam
/// <see cref="RevitTaskQueue.Post"/>, panggilan jaringan hanya di luar itu.
/// </summary>
public sealed class SyncController : IDisposable
{
    private readonly RevitTaskQueue _queue;
    private readonly SupabaseClient _client;
    private readonly CircuitSyncApi _api;
    private readonly AddinSettings _settings;
    private readonly System.Timers.Timer _timer;

    /// <summary>Model harus diam selama ini dulu sebelum tarikan otomatis berjalan.</summary>
    private static readonly TimeSpan QuietPeriod = TimeSpan.FromSeconds(10);

    /// <summary>Jarak minimum antar tarikan otomatis, seaktif apa pun model dikerjakan.</summary>
    private static readonly TimeSpan MinimumAutoPushGap = TimeSpan.FromSeconds(60);

    private int _busy;
    private int _modelDirty;
    private long _lastChangeTicks;
    private DateTime _lastAutoPushAt = DateTime.MinValue;

    /// <summary>
    /// Project dan sidik jari tarikan terakhir yang berhasil. Dipakai membuang unggahan
    /// yang isinya sama persis dengan yang sudah ada di cloud.
    /// </summary>
    private (Guid Project, string Fingerprint)? _lastPush;

    public SyncController(RevitTaskQueue queue, AddinSettings settings, SupabaseClient? client = null)
    {
        _queue = queue;
        _settings = settings;
        _client = client ?? new SupabaseClient();
        _api = new CircuitSyncApi(_client);

        _timer = new System.Timers.Timer { AutoReset = true };
        _timer.Elapsed += (_, _) => Tick();

        _queue.OnError = ex => Log(LogKind.Error, "log.apply_failed", ex.Message);
    }

    public event Action? StateChanged;

    public event Action<LogEntry>? Logged;

    public SupabaseConfig Config => _client.Config;

    public bool IsSignedIn => _client.IsSignedIn;

    public string? UserEmail => _client.UserEmail;

    public Guid? ProjectId { get; private set; }

    public string? ProjectName { get; private set; }

    public IReadOnlyList<ProjectRow> Projects { get; private set; } = [];

    public ModelSnapshot? LastSnapshot { get; private set; }

    public bool Busy => Volatile.Read(ref _busy) > 0;

    // ---------------------------------------------------------------- lifecycle

    public async Task InitializeAsync()
    {
        using (Working())
        {
            if (await _client.TryRestoreAsync().ConfigureAwait(false))
            {
                await ReloadProjectsAsync().ConfigureAwait(false);
            }
        }

        RefreshBinding();
    }

    public void SetAutoPoll(bool enabled, int seconds)
    {
        _settings.AutoPoll = enabled;
        _settings.PollSeconds = seconds;
        _settings.Save();
        RestartTimer();
    }

    public void SetAutoPush(bool enabled)
    {
        _settings.AutoPush = enabled;
        _settings.Save();
        RestartTimer();
    }

    private void RestartTimer()
    {
        _timer.Interval = Math.Max(5, _settings.PollSeconds) * 1000d;
        _timer.Enabled = _settings.AutoPoll || _settings.AutoPush;
    }

    /// <summary>
    /// Menandai bahwa ada device, panel, atau family yang berubah di Revit. Dipanggil dari
    /// event <c>DocumentChanged</c>, jadi harus murah — pekerjaannya menunggu detak timer.
    /// </summary>
    public void NoteModelChanged()
    {
        Interlocked.Exchange(ref _modelDirty, 1);
        Interlocked.Exchange(ref _lastChangeTicks, DateTime.UtcNow.Ticks);
    }

    /// <summary>
    /// Satu detak: kirim ulang model kalau perlu, lalu ambil rencana dari web.
    /// </summary>
    /// <remarks>
    /// Urutannya disengaja. Rencana dari web menunjuk device lewat <c>UniqueId</c>, jadi
    /// mengirim model lebih dulu membuat rencana yang datang divalidasi terhadap keadaan
    /// terbaru — bukan terhadap model yang sudah berubah sejak tarikan terakhir.
    ///
    /// Detak dilewati selagi ada pekerjaan berjalan. Snapshot model besar bisa lebih lama
    /// dari intervalnya, dan menumpuknya hanya menghasilkan antrean request yang saling
    /// mendahului.
    /// </remarks>
    private void Tick()
    {
        if (Busy)
        {
            return;
        }

        if (ShouldAutoPush())
        {
            PushSnapshot(quiet: true);
        }

        if (_settings.AutoPoll)
        {
            CheckJobs(quiet: true);
        }
    }

    /// <summary>
    /// Menahan tarikan otomatis supaya tidak berjalan di tengah pekerjaan user.
    /// </summary>
    /// <remarks>
    /// <see cref="ModelReader.Read"/> berjalan di thread utama Revit: ia memindai seluruh
    /// fixture dan menghitung isi tiap view denah. Menjalankannya tiap detak selama user
    /// menggambar akan terasa sebagai Revit yang tersendat berkala.
    ///
    /// Dua rem. <b>Jeda tenang</b> — model harus diam dulu, jadi tarikan terjadi di sela
    /// pekerjaan, bukan di tengahnya. <b>Jarak minimum</b> — sesi menggambar panjang yang
    /// penuh jeda pendek tetap tidak menghasilkan tarikan beruntun.
    ///
    /// Tanda kotor sengaja tidak dibersihkan di sini, melainkan setelah tarikan benar-benar
    /// selesai. Kegagalan jaringan tidak boleh membuat perubahan hilang diam-diam.
    /// </remarks>
    private bool ShouldAutoPush()
    {
        if (!_settings.AutoPush || !_client.IsSignedIn || Volatile.Read(ref _modelDirty) == 0)
        {
            return false;
        }

        var now = DateTime.UtcNow;
        var lastChange = new DateTime(Interlocked.Read(ref _lastChangeTicks), DateTimeKind.Utc);

        if (now - lastChange < QuietPeriod || now - _lastAutoPushAt < MinimumAutoPushGap)
        {
            return false;
        }

        _lastAutoPushAt = now;
        return true;
    }

    // ---------------------------------------------------------------- auth

    public Task SignInWithPasswordAsync(string email, string password) =>
        Guarded(async () =>
        {
            await _client.SignInWithPasswordAsync(email.Trim(), password).ConfigureAwait(false);
            Log(LogKind.Ok, "auth.signed_in_as", _client.UserEmail ?? email);
            await ReloadProjectsAsync().ConfigureAwait(false);
            RefreshBinding();
        }, "auth.failed");

    public Task SendEmailCodeAsync(string email) =>
        Guarded(async () =>
        {
            await _client.SendEmailCodeAsync(email.Trim()).ConfigureAwait(false);
            Log(LogKind.Info, "auth.code_sent", email.Trim());
        }, "auth.failed");

    public Task VerifyEmailCodeAsync(string email, string code) =>
        Guarded(async () =>
        {
            await _client.VerifyEmailCodeAsync(email.Trim(), code).ConfigureAwait(false);
            Log(LogKind.Ok, "auth.signed_in_as", _client.UserEmail ?? email);
            await ReloadProjectsAsync().ConfigureAwait(false);
            RefreshBinding();
        }, "auth.code_failed");

    public Task SignOutAsync() =>
        Guarded(async () =>
        {
            await _client.SignOutAsync().ConfigureAwait(false);
            Projects = [];
            ProjectName = null;
            SetAutoPoll(false, _settings.PollSeconds);
            Log(LogKind.Info, "auth.signed_out");
        }, "auth.failed");

    // ---------------------------------------------------------------- project

    public Task ReloadProjectsAsync() =>
        Guarded(async () =>
        {
            Projects = await _api.ListProjectsAsync().ConfigureAwait(false);
            ResolveProjectName();
        }, "log.network");

    /// <summary>
    /// Membuat project di Supabase lalu menuliskan GUID-nya ke dokumen. Urutannya
    /// penting: kalau penulisan ke dokumen gagal, project di cloud masih ada dan bisa
    /// dihubungkan manual — sebaliknya akan meninggalkan dokumen menunjuk project hantu.
    /// </summary>
    public Task CreateProjectAsync(string name) =>
        Guarded(async () =>
        {
            var project = await _api.CreateProjectAsync(name).ConfigureAwait(false);
            Bind(project.Id, project.Name, "project.created");
            Projects = await _api.ListProjectsAsync().ConfigureAwait(false);
        }, "log.network");

    public void LinkProject(Guid projectId)
    {
        var name = Projects.FirstOrDefault(p => p.Id == projectId)?.Name ?? projectId.ToString();
        Bind(projectId, name, "project.linked");
    }

    public void UnlinkProject()
    {
        _queue.Post(app =>
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc is null)
            {
                Log(LogKind.Warn, "log.no_document");
                return;
            }

            using var transaction = new Transaction(doc, "CircuitSync — lepas ikatan project");
            transaction.Start();
            DocumentProjectBinding.Clear(doc);
            transaction.Commit();

            ProjectId = null;
            ProjectName = null;
            Log(LogKind.Info, "project.unlinked");
            Changed();
        });
    }

    /// <summary>Membaca ulang ikatan dari dokumen aktif. Dokumen adalah sumber kebenaran.</summary>
    public void RefreshBinding()
    {
        _queue.Post(app =>
        {
            var doc = app.ActiveUIDocument?.Document;
            ProjectId = doc is null ? null : DocumentProjectBinding.Read(doc);
            ResolveProjectName();
            Changed();
        });
    }

    private void Bind(Guid projectId, string name, string messageKey)
    {
        _queue.Post(app =>
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc is null)
            {
                Log(LogKind.Warn, "log.no_document");
                return;
            }

            using var transaction = new Transaction(doc, "CircuitSync — ikat project");
            transaction.Start();
            DocumentProjectBinding.Write(doc, projectId);
            transaction.Commit();

            ProjectId = projectId;
            ProjectName = name;
            Log(LogKind.Ok, messageKey, name);
            Changed();
        });
    }

    private void ResolveProjectName()
    {
        ProjectName = ProjectId is null
            ? null
            : Projects.FirstOrDefault(p => p.Id == ProjectId)?.Name ?? ProjectName;
    }

    // ---------------------------------------------------------------- snapshot

    public void PushSnapshot(bool quiet = false)
    {
        if (!EnsureSignedIn(quiet))
        {
            return;
        }

        if (!quiet)
        {
            Log(LogKind.Info, "log.push_start");
        }

        _queue.Post(app =>
        {
            if (!TryContext(app, out var doc, out var projectId, quiet))
            {
                return;
            }

            var snapshot = ModelReader.Read(doc);
            LastSnapshot = snapshot;
            Changed();

            RunInBackground(async () =>
            {
                // Sidik jari dihitung di sini, bukan di thread Revit: serialisasi ratusan
                // baris tidak perlu ikut menahan UI.
                var fingerprint = snapshot.Fingerprint();
                var unchanged = _lastPush is { } last &&
                                last.Project == projectId &&
                                last.Fingerprint == fingerprint;

                // Tarikan manual selalu jadi. User yang menekan tombol berhak melihat
                // sesuatu terjadi, dan itu juga satu-satunya jalan memulihkan cloud yang
                // pernah diubah dari luar.
                if (unchanged && quiet)
                {
                    Interlocked.Exchange(ref _modelDirty, 0);
                    return;
                }

                await _api.PushSnapshotAsync(projectId, snapshot).ConfigureAwait(false);

                _lastPush = (projectId, fingerprint);
                Interlocked.Exchange(ref _modelDirty, 0);
                Log(LogKind.Ok, "log.push_done", snapshot.Devices.Count, snapshot.Panels.Count);
                WarnAboutMissingSchema();
            });
        });
    }

    // ---------------------------------------------------------------- apply

    /// <summary>
    /// Menerapkan <b>seluruh</b> job apply yang mengantre, satu per satu sampai habis.
    /// </summary>
    /// <remarks>
    /// Dulu hanya <c>jobs[0]</c> yang dikerjakan, jadi dua kali "Kirim ke Revit" dari web
    /// berarti dua kali klik "Ambil rencana" di sini — dan sisanya diam di antrean tanpa
    /// penjelasan.
    ///
    /// Model dibaca ulang untuk setiap job, bukan sekali di awal: job sebelumnya baru saja
    /// mengubah model, dan memvalidasi job berikutnya terhadap snapshot basi akan menolak
    /// device yang sebenarnya sah.
    /// </remarks>
    public void CheckJobs(bool quiet = false)
    {
        if (!EnsureSignedIn(quiet))
        {
            return;
        }

        RunInBackground(async () =>
        {
            var projectId = await _queue.PostAsync(app => BoundProjectId(app, quiet)).ConfigureAwait(false);
            if (projectId is null)
            {
                return;
            }

            var jobs = await _api.FetchQueuedApplyJobsAsync(projectId.Value).ConfigureAwait(false);

            // Dua antrean, satu detak. Job gambar garis tidak menunggu antrean apply-nya
            // kosong: keduanya datang dari halaman yang sama dan user tidak membedakan
            // "kirim circuit" dari "kirim garis" sebagai dua kunjungan berbeda.
            var wiringJobs = await _api.FetchQueuedWiringJobsAsync(projectId.Value).ConfigureAwait(false);

            if (jobs.Count + wiringJobs.Count == 0)
            {
                if (!quiet)
                {
                    Log(LogKind.Info, "log.no_job");
                }

                return;
            }

            if (jobs.Count + wiringJobs.Count > 1)
            {
                Log(LogKind.Info, "log.jobs_found", jobs.Count + wiringJobs.Count);
            }

            foreach (var job in jobs)
            {
                await ApplyJobAsync(projectId.Value, job).ConfigureAwait(false);
            }

            foreach (var job in wiringJobs)
            {
                await DrawWiringJobAsync(projectId.Value, job).ConfigureAwait(false);
            }
        });
    }

    /// <summary>
    /// Satu job gambar garis: baca payload, gambar di thread Revit, tandai hasilnya.
    /// </summary>
    /// <remarks>
    /// Tidak ada validasi terhadap snapshot seperti pada job apply, dan itu disengaja.
    /// Garis wiring tidak menyambungkan apa pun secara listrik: ia tidak bisa bentrok
    /// dengan circuit lain, tidak butuh slot panel, dan tidak berubah arti kalau sebuah
    /// device dipindahkan. Yang perlu ada hanya view-nya dan line style-nya, dan itu
    /// diperiksa <see cref="WiringApplier"/> terhadap dokumen yang sedang terbuka.
    ///
    /// Pengiriman bersifat <b>mengganti</b>. Garis kiriman sebelumnya untuk denah yang
    /// sama dibaca dari <c>wiring_curves</c>, dihapus dari model, lalu catatannya ditulis
    /// ulang dengan garis yang baru. Tanpa itu kiriman kedua menumpuk di atas yang
    /// pertama — dan kiriman kedua adalah hal yang biasa, karena setiap kali ada
    /// electrical device berubah, wiring di web ikut berubah.
    /// </remarks>
    private async Task DrawWiringJobAsync(Guid projectId, SyncJobRow job)
    {
        if (job.Wiring() is not { } request)
        {
            // Payload jsonb bisa tumbuh tanpa migrasi, jadi add-in versi lama bisa
            // menerima bentuk yang belum dikenalnya. Ditolak dengan jelas, bukan
            // digambar setengah.
            Log(LogKind.Error, WiringPayloadUnreadable);
            await _api.MarkJobFailedAsync(job.Id, WiringPayloadUnreadable).ConfigureAwait(false);
            return;
        }

        // Dibaca sebelum melompat ke thread Revit: ini panggilan jaringan, dan aturan
        // thread di kelas ini melarang jaringan di dalam RevitTaskQueue.Post.
        var existing = await _api.FetchWiringCurvesAsync(projectId, request.LayoutUniqueId)
            .ConfigureAwait(false);

        var result = await _queue.PostAsync<WiringApplyResult?>(app =>
            app.ActiveUIDocument?.Document is { } doc
                ? WiringApplier.Apply(doc, request, existing)
                : null).ConfigureAwait(false);

        if (result is null)
        {
            Log(LogKind.Warn, "log.no_document");
            return;
        }

        if (result.Ok)
        {
            // Catatan ditulis sebelum job ditandai selesai. Kalau urutannya dibalik dan
            // penulisan catatan gagal, job sudah tampak berhasil di web sementara garis
            // yang baru digambar tidak tercatat — dan kiriman berikutnya menumpuk lagi.
            await _api.ReplaceWiringCurvesAsync(projectId, request.LayoutUniqueId, result.Drawn)
                .ConfigureAwait(false);

            await _api.MarkJobAppliedAsync(job.Id).ConfigureAwait(false);

            Log(LogKind.Ok, result.LinesErased > 0 ? "log.wiring_replaced" : "log.wiring_done",
                result.RunsDrawn, result.LinesDrawn, result.LinesErased);

            // Peringatannya sempit, bukan seluruh daftar tabel yang hilang: yang relevan di
            // sini cuma satu, dan akibatnya khas — garis akan menumpuk di kiriman berikutnya
            // karena tidak ada yang mencatat garis yang baru digambar.
            if (_api.MissingTables.Contains("wiring_curves"))
            {
                Log(LogKind.Warn, "log.wiring_untracked");
            }

            return;
        }

        var error = string.IsNullOrWhiteSpace(result.ErrorDetail)
            ? result.ErrorKey ?? ""
            : $"{result.ErrorKey}\n{result.ErrorDetail}";

        await _api.MarkJobFailedAsync(job.Id, error).ConfigureAwait(false);

        // Kunci pesannya jadi pesannya, bukan argumen di dalam pesan lain: kunci mentah
        // seperti `wiring.layout_not_plan` di tengah kalimat tidak berarti apa pun bagi
        // yang membacanya. Keterangan dari Revit ikut, di barisnya sendiri — kalimat
        // Revit-lah yang membedakan style yang hilang dari kurva yang ditolak.
        Log(LogKind.Error, result.ErrorKey ?? WiringPayloadUnreadable);

        if (!string.IsNullOrWhiteSpace(result.ErrorDetail))
        {
            Log(LogKind.Warn, "log.wiring_detail", result.ErrorDetail);
        }
    }

    /// <summary>Kunci untuk payload wiring yang bentuknya tidak dikenali.</summary>
    private const string WiringPayloadUnreadable = "wiring.payload_unreadable";

    /// <summary>
    /// Satu job, empat lompatan thread: baca model (Revit) → ambil rencana (jaringan) →
    /// apply (Revit) → tulis balik (jaringan).
    /// </summary>
    private async Task ApplyJobAsync(Guid projectId, SyncJobRow job)
    {
        // Generic ditulis eksplisit: tanpa itu T tersimpul non-nullable dan cabang null
        // di dalam lambda memicu CS8603, yang di CI diperlakukan sebagai error.
        var snapshot = await _queue.PostAsync<ModelSnapshot?>(app =>
            app.ActiveUIDocument?.Document is { } doc ? ModelReader.Read(doc) : null).ConfigureAwait(false);

        if (snapshot is null)
        {
            Log(LogKind.Warn, "log.no_document");
            return;
        }

        LastSnapshot = snapshot;
        Changed();

        var circuits = await _api.FetchCircuitsAsync(projectId, job.CircuitIds().ToList()).ConfigureAwait(false);

        var validation = PlanValidator.Validate(circuits, snapshot);
        foreach (var problem in validation.Problems)
        {
            Log(LogKind.Warn, "log.job_error", problem.MessageKey);
        }

        if (validation.Accepted.Count == 0)
        {
            await FailJobAsync(job, validation).ConfigureAwait(false);
            return;
        }

        Log(LogKind.Info, "log.job_found", validation.Accepted.Count);

        var options = new ApplyOptions { PlaceTags = _settings.PlaceTags };
        var results = await _queue.PostAsync<IReadOnlyList<CircuitApplyResult>?>(app =>
            app.ActiveUIDocument?.Document is { } doc
                ? CircuitApplier.Apply(doc, validation.Accepted, options)
                : null).ConfigureAwait(false);

        if (results is null)
        {
            Log(LogKind.Warn, "log.no_document");
            return;
        }

        await WriteBackAsync(projectId, job, validation, results, snapshot).ConfigureAwait(false);
    }

    private async Task WriteBackAsync(Guid projectId, SyncJobRow job, PlanValidation validation,
        IReadOnlyList<CircuitApplyResult> results, ModelSnapshot snapshot)
    {
        var devices = new List<DeviceConnection>();

        foreach (var result in results)
        {
            await _api.WriteCircuitResultAsync(
                result.CircuitId,
                result.Ok ? CircuitStatus.Applied : CircuitStatus.Failed,
                result.CircuitNumber,
                result.RevitUniqueId,
                result.Ok ? result.ErrorDetail : Explain(result)).ConfigureAwait(false);

            if (result.Ok)
            {
                devices.AddRange(result.UpdatedDevices);
                Log(LogKind.Ok, result.Rebuilt ? "log.rebuilt_one" : "log.applied_one",
                    result.CircuitNumber ?? "—", result.UpdatedDevices.Count);
            }
            else
            {
                Log(LogKind.Error, "log.apply_failed", result.ErrorDetail ?? result.ErrorKey ?? "");
            }
        }

        foreach (var rejected in RejectedCircuits(validation))
        {
            await _api.WriteCircuitResultAsync(rejected.Id, CircuitStatus.Failed, null, null,
                FirstProblem(validation, rejected.Id)).ConfigureAwait(false);
        }

        // Device yang dikeluarkan dari circuit saat isinya diubah harus kembali merah di
        // web. Tanpa ini device bekas anggota tetap tampak hijau padahal di model sudah
        // tidak tersambung ke mana pun.
        //
        // Yang sudah masuk circuit lain di job yang sama dikecualikan: satu device bisa
        // dilepas dari circuit A dan dipakai circuit B sekaligus, dan menulis "unwired"
        // setelah "connected" akan mengembalikannya jadi merah padahal baru tersambung.
        var connected = devices.Select(d => d.RevitUniqueId).ToHashSet(StringComparer.Ordinal);
        devices.AddRange(ReleasedDevices(validation, snapshot, results)
            .Where(released => !connected.Contains(released.RevitUniqueId)));

        if (devices.Count > 0)
        {
            // Status device disegarkan dari model, bukan dari asumsi: nomor circuit
            // yang ditulis di sini berasal dari ElectricalSystem.CircuitNumber.
            await _api.UpdateDeviceConnectionsAsync(projectId, devices).ConfigureAwait(false);
        }

        var applied = results.Count(r => r.Ok);
        if (applied == 0)
        {
            await _api.MarkJobFailedAsync(job.Id, "no_circuit_applied").ConfigureAwait(false);
        }
        else
        {
            await _api.MarkJobAppliedAsync(job.Id).ConfigureAwait(false);
            Log(LogKind.Ok, "log.applied", applied);
        }
    }

    private async Task FailJobAsync(SyncJobRow job, PlanValidation validation)
    {
        foreach (var rejected in RejectedCircuits(validation))
        {
            await _api.WriteCircuitResultAsync(rejected.Id, CircuitStatus.Failed, null, null,
                FirstProblem(validation, rejected.Id)).ConfigureAwait(false);
        }

        await _api.MarkJobFailedAsync(job.Id, "plan_rejected").ConfigureAwait(false);
    }

    /// <summary>
    /// Device yang tadinya anggota circuit yang baru saja dibangun ulang, tapi tidak ikut
    /// di isi barunya. Keanggotaan lama dibaca dari snapshot sebelum apply — bukan dari
    /// Revit setelahnya, karena circuit lamanya sudah tidak ada.
    /// </summary>
    /// <summary>
    /// Menyebut kolom dan tabel yang dilewati karena database belum mengenalnya.
    /// </summary>
    /// <remarks>
    /// Tarikan modelnya berhasil — itu sebabnya barisnya peringatan, bukan kesalahan.
    /// Tapi fitur yang bergantung padanya tidak akan jalan di web, dan tanpa baris ini
    /// satu-satunya gejalanya adalah layar yang isinya kurang tanpa sebab yang terlihat.
    /// Disebutkan sekali per kolom dan per tabel, bukan sekali per permintaan.
    ///
    /// Tabel dan kolom dipisah jadi dua baris karena jalan keluarnya sama tapi
    /// akibatnya tidak: kolom yang hilang membuat sebagian isi tabel kosong, tabel yang
    /// hilang membuat seluruh fiturnya tidak ada.
    /// </remarks>
    private void WarnAboutMissingSchema()
    {
        var columns = _api.Client.MissingColumns;
        if (columns.Count > 0)
        {
            Log(LogKind.Warn, "log.missing_columns", string.Join(", ", columns));
        }

        var tables = _api.MissingTables;
        if (tables.Count > 0)
        {
            Log(LogKind.Warn, "log.missing_tables", string.Join(", ", tables));
        }
    }

    private static IEnumerable<DeviceConnection> ReleasedDevices(PlanValidation validation, ModelSnapshot snapshot,
        IReadOnlyList<CircuitApplyResult> results)
    {
        var applied = results.Where(r => r.Ok).Select(r => r.CircuitId).ToHashSet();

        foreach (var circuit in validation.Accepted)
        {
            if (string.IsNullOrEmpty(circuit.RevitUniqueId) || !applied.Contains(circuit.Id))
            {
                continue;
            }

            var kept = circuit.DeviceUniqueIds.ToHashSet(StringComparer.Ordinal);

            foreach (var (deviceId, systemId) in snapshot.DeviceSystems)
            {
                if (string.Equals(systemId, circuit.RevitUniqueId, StringComparison.Ordinal) &&
                    !kept.Contains(deviceId))
                {
                    yield return new DeviceConnection
                    {
                        RevitUniqueId = deviceId,
                        Status = DeviceStatus.Unwired,
                        CircuitNumber = null,
                        // Null ikut ditulis, dan di sini itu justru gunanya: device yang
                        // dilepas harus kehilangan panelnya juga, bukan tetap terhitung
                        // sebagai isi panel yang sudah tidak memuatnya.
                        PanelUniqueId = null,
                    };
                }
            }
        }
    }

    /// <summary>
    /// Isi kolom <c>circuits.error</c> saat sebuah circuit gagal: kunci pesan di baris
    /// pertama, penjelasan mentah dari Revit di baris berikutnya.
    /// </summary>
    /// <remarks>
    /// Kunci saja tidak cukup. "Ditolak Revit" benar tapi tidak berguna — yang
    /// membedakan panel penuh dari tegangan yang tidak cocok hanya ada di pesan Revit,
    /// dan dulu pesan itu ditangkap lalu dibuang. Dipisah baris supaya web tetap bisa
    /// menerjemahkan barisan pertamanya, dan menampilkan sisanya apa adanya.
    /// </remarks>
    private static string Explain(CircuitApplyResult result)
    {
        var key = result.ErrorKey ?? "";
        return string.IsNullOrWhiteSpace(result.ErrorDetail) ? key : $"{key}\n{result.ErrorDetail}";
    }

    private static IEnumerable<CircuitRow> RejectedCircuits(PlanValidation validation) =>
        validation.Problems
            .Select(p => p.CircuitId)
            .Distinct()
            .Where(id => validation.Accepted.All(a => a.Id != id))
            .Select(id => new CircuitRow { Id = id });

    private static string FirstProblem(PlanValidation validation, Guid circuitId) =>
        validation.Problems.First(p => p.CircuitId == circuitId).MessageKey;

    // ---------------------------------------------------------------- plumbing

    /// <summary>
    /// Versi <see cref="TryContext"/> yang hanya mengembalikan project id, untuk alur
    /// yang membaca dokumennya sendiri di tiap lompatan ke thread Revit.
    /// </summary>
    private Guid? BoundProjectId(UIApplication app, bool quiet) =>
        TryContext(app, out _, out var projectId, quiet) ? projectId : null;

    private bool TryContext(UIApplication app, out Document doc, out Guid projectId, bool quiet = false)
    {
        doc = null!;
        projectId = Guid.Empty;

        var active = app.ActiveUIDocument?.Document;
        if (active is null)
        {
            if (!quiet)
            {
                Log(LogKind.Warn, "log.no_document");
            }

            return false;
        }

        var bound = DocumentProjectBinding.Read(active);
        if (bound is null)
        {
            if (!quiet)
            {
                Log(LogKind.Warn, "log.no_project");
            }

            return false;
        }

        doc = active;
        projectId = bound.Value;
        ProjectId = bound;
        return true;
    }

    private bool EnsureSignedIn(bool quiet = false)
    {
        if (_client.IsSignedIn)
        {
            return true;
        }

        if (!quiet)
        {
            Log(LogKind.Warn, "log.not_signed_in");
        }

        return false;
    }

    private void RunInBackground(Func<Task> work)
    {
        var scope = Working();
        _ = Task.Run(async () =>
        {
            try
            {
                await work().ConfigureAwait(false);
            }
            catch (CloudException ex)
            {
                LogCloud(ex);
            }
            catch (Exception ex)
            {
                // Pekerjaan di thread Revit sekarang dilempar balik ke sini lewat
                // RevitTaskQueue.PostAsync, bukan ditelan RevitTaskQueue.OnError. Tanpa
                // jaring ini kegagalannya jadi exception yang tidak pernah diamati:
                // job berikutnya tidak jalan, dan log tetap kosong.
                Log(LogKind.Error, "log.apply_failed", ex.Message);
            }
            finally
            {
                scope.Dispose();
            }
        });
    }

    private async Task Guarded(Func<Task> work, string failureKey)
    {
        using (Working())
        {
            try
            {
                await work().ConfigureAwait(false);
            }
            catch (CloudException ex)
            {
                Log(LogKind.Error, ex.Message == "network" ? "log.network" : failureKey);
            }
        }
    }

    private void LogCloud(CloudException ex) =>
        Log(LogKind.Error, ex.Message is "network" or "timeout" ? "log.network" : "log.job_error", ex.Describe());

    private void Log(LogKind kind, string key, params object?[] args)
    {
        Logged?.Invoke(new LogEntry(DateTime.Now, kind, key, args));
    }

    private void Changed() => StateChanged?.Invoke();

    private IDisposable Working()
    {
        Interlocked.Increment(ref _busy);
        Changed();
        return new Scope(this);
    }

    private sealed class Scope(SyncController owner) : IDisposable
    {
        public void Dispose()
        {
            Interlocked.Decrement(ref owner._busy);
            owner.Changed();
        }
    }

    public void Dispose()
    {
        _timer.Dispose();
        _client.Dispose();
    }
}
