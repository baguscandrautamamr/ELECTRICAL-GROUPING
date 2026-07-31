using Autodesk.Revit.DB;
using CircuitSync.Core;

namespace CircuitSync.Revit;

public sealed record WiringApplyResult
{
    public bool Ok { get; init; }

    /// <summary>Kaki saklar yang berhasil digambar seluruhnya.</summary>
    public int RunsDrawn { get; init; }

    /// <summary>Detail curve yang benar-benar masuk model. Satu ruas = satu curve.</summary>
    public int LinesDrawn { get; init; }

    /// <summary>Garis kiriman sebelumnya yang dihapus dari model.</summary>
    public int LinesErased { get; init; }

    /// <summary>
    /// Garis yang baru digambar, untuk dicatat ke database sebagai isi denah saat ini.
    /// </summary>
    public IReadOnlyList<WiringCurveRow> Drawn { get; init; } = [];

    /// <summary>Kunci pesan untuk UI, bukan teks siap tampil.</summary>
    public string? ErrorKey { get; init; }

    public string? ErrorDetail { get; init; }
}

/// <summary>
/// Menggambar garis wiring yang diminta web sebagai detail curve di satu view denah.
/// </summary>
/// <remarks>
/// Lapisan ini menerjemahkan, tidak memutuskan. Titik-titiknya sudah selesai dihitung
/// di <c>web/lib/wiring.ts</c> dan dipakai apa adanya — yang dikerjakan di sini hanya
/// milimeter ke satuan internal, dan memasang line style pilihan user.
///
/// <b>Detail curve, bukan model curve.</b> Garis wiring adalah anotasi denah: ia hidup
/// di satu view, ikut skalanya, dan tidak boleh muncul di view lain atau di 3D. Model
/// curve akan tampil di mana-mana dan menabrak geometri sungguhan.
///
/// <b>Mengganti, bukan menambah.</b> Garis kiriman sebelumnya dihapus lebih dulu, dan
/// yang dihapus hanya yang tercatat di <c>wiring_curves</c> — bukan semua detail curve
/// di view, dan bukan berdasarkan line style. Keduanya akan ikut membuang garis yang
/// digambar user sendiri.
/// </remarks>
public static class WiringApplier
{
    public const string ErrorLayoutMissing = "wiring.layout_missing";
    public const string ErrorLayoutNotPlan = "wiring.layout_not_plan";
    public const string ErrorLineStyleMissing = "wiring.line_style_missing";
    public const string ErrorNothingDrawn = "wiring.nothing_drawn";

    /// <param name="existing">
    /// <c>UniqueId</c> garis kiriman sebelumnya untuk denah ini, dibaca dari
    /// <c>wiring_curves</c>. Yang sudah tidak ada di model dilewati tanpa keluhan — user
    /// boleh saja sudah menghapusnya sendiri.
    /// </param>
    public static WiringApplyResult Apply(Document doc, WiringRequest request,
        IReadOnlyList<string> existing)
    {
        if (doc.GetElement(request.LayoutUniqueId) is not { } layoutElement)
        {
            return Failed(ErrorLayoutMissing);
        }

        // Detail curve hanya sah di view yang mendukungnya, dan layout kita memang
        // selalu ViewPlan — lihat ModelReader.ReadLayouts. Kalau yang tersimpan ternyata
        // bukan view denah, itu dilaporkan, bukan dipaksakan.
        if (layoutElement is not ViewPlan view)
        {
            return Failed(ErrorLayoutNotPlan);
        }

        if (doc.GetElement(request.LineStyleUniqueId) is not GraphicsStyle style)
        {
            return Failed(ErrorLineStyleMissing);
        }

        // Ruas yang lebih pendek daripada toleransi Revit ditolak dengan exception, jadi
        // ia dibuang lebih dulu. Ambangnya dibaca dari Revit, bukan ditulis sebagai angka:
        // toleransi itu milik aplikasi, dan menebaknya berarti salah di satu sisi — terlalu
        // kecil menghasilkan exception, terlalu besar membuang ruas yang sah.
        var shortest = doc.Application.ShortCurveTolerance;

        var elevation = view.GenLevel?.ProjectElevation ?? 0;
        var runsDrawn = 0;
        var linesErased = 0;
        var drawnRows = new List<WiringCurveRow>();
        var problems = new List<string>();

        // Satu TransactionGroup untuk hapus **dan** gambar, lalu Assimilate: satu Ctrl+Z
        // mengembalikan denah ke keadaan sebelum pengiriman. Kalau keduanya dipisah, sekali
        // undo hanya membuang garis baru dan meninggalkan denah tanpa garis sama sekali.
        using var group = new TransactionGroup(doc, "CircuitSync — gambar garis wiring");
        group.Start();

        using (var transaction = new Transaction(doc, "CircuitSync — garis wiring"))
        {
            transaction.Start();

            linesErased = Erase(doc, existing);

            foreach (var run in request.Runs)
            {
                // Satu SubTransaction per kaki: satu kaki yang ditolak tidak boleh
                // menggugurkan sisanya, dan yang setengah tergambar tidak boleh tertinggal.
                var sub = new SubTransaction(doc);
                sub.Start();

                try
                {
                    var drawn = DrawRun(doc, view, style, run, elevation, shortest);
                    if (drawn.Count == 0)
                    {
                        sub.RollBack();
                        continue;
                    }

                    sub.Commit();
                    runsDrawn++;

                    // Dicatat setelah commit: id elemen yang transaksinya digulung tidak
                    // menunjuk apa pun, dan menuliskannya ke database akan membuat
                    // pengiriman berikutnya mencari garis yang tidak pernah ada.
                    foreach (var uniqueId in drawn)
                    {
                        drawnRows.Add(new WiringCurveRow
                        {
                            LayoutUniqueId = request.LayoutUniqueId,
                            RevitUniqueId = uniqueId,
                            SwitchIndex = run.SwitchIndex,
                        });
                    }
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException ex)
                {
                    RollBackQuietly(sub);
                    problems.Add($"saklar {run.SwitchIndex + 1}: {ex.Message}");
                }
                finally
                {
                    sub.Dispose();
                }
            }

            transaction.Commit();
        }

        group.Assimilate();

        var linesDrawn = drawnRows.Count;

        var detail = problems.Count == 0 ? null : string.Join(" · ", problems);

        return linesDrawn == 0
            ? Failed(ErrorNothingDrawn, detail) with { LinesErased = linesErased }
            : new WiringApplyResult
            {
                Ok = true,
                RunsDrawn = runsDrawn,
                LinesDrawn = linesDrawn,
                LinesErased = linesErased,
                Drawn = drawnRows,
                ErrorDetail = detail,
            };
    }

    /// <summary>
    /// Membuang garis kiriman sebelumnya. Mengembalikan berapa yang benar-benar terhapus.
    /// </summary>
    /// <remarks>
    /// Yang sudah tidak ada di model dilewati tanpa keluhan: user boleh menghapus garis
    /// sendiri, dan itu bukan kegagalan — hasil akhirnya tetap sama, yaitu denah yang
    /// hanya memuat garis kiriman terbaru.
    ///
    /// Dihapus sekaligus dalam satu panggilan, bukan satu per satu, supaya Revit hanya
    /// sekali menghitung ulang ketergantungan elemen.
    /// </remarks>
    private static int Erase(Document doc, IReadOnlyList<string> existing)
    {
        var ids = new List<ElementId>();

        foreach (var uniqueId in existing)
        {
            if (doc.GetElement(uniqueId) is { } element)
            {
                ids.Add(element.Id);
            }
        }

        if (ids.Count == 0)
        {
            return 0;
        }

        return doc.Delete(ids).Count;
    }

    /// <summary>
    /// Satu kaki: polyline dipecah jadi ruas, tiap ruas satu detail curve dengan line
    /// style yang sama.
    /// </summary>
    /// <remarks>
    /// Revit tidak punya "detail polyline" — <c>NewDetailCurve</c> menerima satu curve.
    /// Memecahnya di sini, bukan menyambungnya jadi satu, disengaja: user bisa menghapus
    /// atau memindahkan satu ruas tanpa kehilangan seluruh kaki.
    /// </remarks>
    private static List<string> DrawRun(Document doc, ViewPlan view, GraphicsStyle style, WireRunRow run,
        double elevation, double shortest)
    {
        var drawn = new List<string>();

        for (var index = 1; index < run.Vertices.Count; index++)
        {
            var from = PointOf(run.Vertices[index - 1], elevation);
            var to = PointOf(run.Vertices[index], elevation);

            if (from.DistanceTo(to) < shortest)
            {
                continue;
            }

            var curve = doc.Create.NewDetailCurve(view, Line.CreateBound(from, to));
            curve.LineStyle = style;
            drawn.Add(curve.UniqueId);
        }

        return drawn;
    }

    /// <summary>
    /// Titik kontrak (milimeter, koordinat model) ke satuan internal Revit.
    /// </summary>
    /// <remarks>
    /// Z diambil dari level view-nya, bukan nol. Detail curve harus sebidang dengan
    /// view-nya, dan denah yang levelnya tidak di elevasi nol akan menolak kurva di Z=0.
    /// </remarks>
    private static XYZ PointOf(WirePoint point, double elevation) => new(
        Units.FromMillimeters(point.XMm),
        Units.FromMillimeters(point.YMm),
        elevation);

    private static void RollBackQuietly(SubTransaction sub)
    {
        try
        {
            if (sub.HasStarted() && !sub.HasEnded())
            {
                sub.RollBack();
            }
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException)
        {
        }
    }

    private static WiringApplyResult Failed(string key, string? detail = null) => new()
    {
        Ok = false,
        ErrorKey = key,
        ErrorDetail = detail,
    };
}
