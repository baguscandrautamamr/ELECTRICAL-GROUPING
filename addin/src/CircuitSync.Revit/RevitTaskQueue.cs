using System.Collections.Concurrent;
using Autodesk.Revit.UI;

namespace CircuitSync.Revit;

/// <summary>
/// Satu-satunya jalan masuk ke Revit API dari luar thread utama.
/// Window kita non-modal, jadi event handler UI dan timer polling TIDAK BOLEH
/// memanggil Revit API langsung — semuanya lewat <see cref="Post"/>, yang menaruh
/// pekerjaan di antrean lalu <see cref="ExternalEvent.Raise"/>.
/// </summary>
public sealed class RevitTaskQueue : IExternalEventHandler
{
    private readonly ConcurrentQueue<Action<UIApplication>> _queue = new();
    private ExternalEvent? _event;

    /// <summary>Dipanggil dari thread utama Revit, sekali, sebelum <see cref="Post"/> dipakai.</summary>
    public void Initialize()
    {
        _event ??= ExternalEvent.Create(this);
    }

    /// <summary>
    /// Melaporkan pekerjaan yang meledak. Diisi UI supaya kegagalan muncul di log,
    /// bukan hilang begitu saja.
    /// </summary>
    public Action<Exception>? OnError { get; set; }

    public void Post(Action<UIApplication> work)
    {
        _queue.Enqueue(work);
        _event?.Raise();
    }

    public void Execute(UIApplication app)
    {
        while (_queue.TryDequeue(out var work))
        {
            try
            {
                work(app);
            }
            catch (Exception ex)
            {
                // Exception yang lolos dari Execute memunculkan dialog crash Revit.
                // Sekalipun kasar, menangkap semuanya di sini lebih baik daripada
                // menjatuhkan sesi kerja user.
                OnError?.Invoke(ex);
            }
        }
    }

    public string GetName() => "CircuitSync";
}
