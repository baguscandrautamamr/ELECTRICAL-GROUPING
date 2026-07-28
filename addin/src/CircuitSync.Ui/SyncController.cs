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

    private int _busy;

    public SyncController(RevitTaskQueue queue, AddinSettings settings, SupabaseClient? client = null)
    {
        _queue = queue;
        _settings = settings;
        _client = client ?? new SupabaseClient();
        _api = new CircuitSyncApi(_client);

        _timer = new System.Timers.Timer { AutoReset = true };
        _timer.Elapsed += (_, _) => CheckJobs(quiet: true);

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

        _timer.Interval = Math.Max(5, seconds) * 1000d;
        _timer.Enabled = enabled;
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

    public void PushSnapshot()
    {
        if (!EnsureSignedIn())
        {
            return;
        }

        Log(LogKind.Info, "log.push_start");

        _queue.Post(app =>
        {
            if (!TryContext(app, out var doc, out var projectId))
            {
                return;
            }

            var snapshot = ModelReader.Read(doc);
            LastSnapshot = snapshot;
            Changed();

            RunInBackground(async () =>
            {
                await _api.PushSnapshotAsync(projectId, snapshot).ConfigureAwait(false);
                Log(LogKind.Ok, "log.push_done", snapshot.Devices.Count, snapshot.Panels.Count);
            });
        });
    }

    // ---------------------------------------------------------------- apply

    /// <summary>
    /// Mengambil satu job dari antrean dan menerapkannya. Tiga lompatan thread:
    /// baca model (thread Revit) → ambil rencana (jaringan) → apply (thread Revit)
    /// → tulis balik (jaringan).
    /// </summary>
    public void CheckJobs(bool quiet = false)
    {
        if (!EnsureSignedIn(quiet))
        {
            return;
        }

        _queue.Post(app =>
        {
            if (!TryContext(app, out var doc, out var projectId, quiet))
            {
                return;
            }

            var snapshot = ModelReader.Read(doc);
            LastSnapshot = snapshot;

            RunInBackground(async () =>
            {
                var jobs = await _api.FetchQueuedApplyJobsAsync(projectId).ConfigureAwait(false);
                if (jobs.Count == 0)
                {
                    if (!quiet)
                    {
                        Log(LogKind.Info, "log.no_job");
                    }

                    return;
                }

                var job = jobs[0];
                var circuits = await _api.FetchCircuitsAsync(projectId, job.CircuitIds().ToList())
                    .ConfigureAwait(false);

                var validation = PlanValidator.Validate(circuits, snapshot);
                foreach (var problem in validation.Problems)
                {
                    Log(LogKind.Warn, "log.job_error", problem.MessageKey);
                }

                if (validation.Accepted.Count == 0)
                {
                    await FailJobAsync(projectId, job, validation).ConfigureAwait(false);
                    return;
                }

                Log(LogKind.Info, "log.job_found", validation.Accepted.Count);
                ApplyOnRevitThread(projectId, job, validation);
            });
        });
    }

    private void ApplyOnRevitThread(Guid projectId, SyncJobRow job, PlanValidation validation)
    {
        _queue.Post(app =>
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc is null)
            {
                Log(LogKind.Warn, "log.no_document");
                return;
            }

            var options = new ApplyOptions { PlaceTags = _settings.PlaceTags };
            var results = CircuitApplier.Apply(doc, validation.Accepted, options);

            RunInBackground(async () =>
            {
                await WriteBackAsync(projectId, job, validation, results).ConfigureAwait(false);
            });
        });
    }

    private async Task WriteBackAsync(Guid projectId, SyncJobRow job, PlanValidation validation,
        IReadOnlyList<CircuitApplyResult> results)
    {
        var devices = new List<DeviceRow>();

        foreach (var result in results)
        {
            await _api.WriteCircuitResultAsync(
                result.CircuitId,
                result.Ok ? CircuitStatus.Applied : CircuitStatus.Failed,
                result.CircuitNumber,
                result.RevitUniqueId,
                result.Ok ? result.ErrorDetail : result.ErrorKey).ConfigureAwait(false);

            if (result.Ok)
            {
                devices.AddRange(result.UpdatedDevices);
                Log(LogKind.Ok, "log.applied_one", result.CircuitNumber ?? "—", result.UpdatedDevices.Count);
            }
            else
            {
                Log(LogKind.Error, "log.apply_failed", result.ErrorKey ?? "");
            }
        }

        foreach (var rejected in RejectedCircuits(validation))
        {
            await _api.WriteCircuitResultAsync(rejected.Id, CircuitStatus.Failed, null, null,
                FirstProblem(validation, rejected.Id)).ConfigureAwait(false);
        }

        if (devices.Count > 0)
        {
            // Status device disegarkan dari model, bukan dari asumsi: nomor circuit
            // yang ditulis di sini berasal dari ElectricalSystem.CircuitNumber.
            await _api.UpdateDevicesAsync(projectId, devices).ConfigureAwait(false);
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

    private async Task FailJobAsync(Guid projectId, SyncJobRow job, PlanValidation validation)
    {
        foreach (var rejected in RejectedCircuits(validation))
        {
            await _api.WriteCircuitResultAsync(rejected.Id, CircuitStatus.Failed, null, null,
                FirstProblem(validation, rejected.Id)).ConfigureAwait(false);
        }

        await _api.MarkJobFailedAsync(job.Id, "plan_rejected").ConfigureAwait(false);
        _ = projectId;
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
        Log(LogKind.Error, ex.Message is "network" or "timeout" ? "log.network" : "log.job_error", ex.Message);

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
