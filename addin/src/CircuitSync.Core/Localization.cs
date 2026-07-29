using System.Globalization;

namespace CircuitSync.Core;

public enum Language
{
    Id,
    En,
}

/// <summary>
/// Terjemahan add-in. Sengaja di Core, bukan di lapisan WPF: teks adalah data,
/// dan kelengkapan kuncinya bisa dites.
/// </summary>
public sealed class Translator
{
    private readonly IReadOnlyDictionary<string, string> _id;
    private readonly IReadOnlyDictionary<string, string> _en;

    public Translator(Language language = Language.Id)
    {
        Language = language;
        _id = Translations.Id;
        _en = Translations.En;
    }

    public Language Language { get; set; }

    public string this[string key] => Get(key);

    /// <summary>
    /// Kunci yang belum diterjemahkan dikembalikan apa adanya, bukan string kosong —
    /// supaya kelihatan di layar dan bukan hilang tanpa jejak.
    /// </summary>
    public string Get(string key)
    {
        var table = Language == Language.En ? _en : _id;
        if (table.TryGetValue(key, out var value))
        {
            return value;
        }

        return Language == Language.En && _id.TryGetValue(key, out var fallback) ? fallback : key;
    }

    public string Get(string key, params object?[] args)
    {
        var format = Get(key);
        try
        {
            return string.Format(CultureInfo.InvariantCulture, format, args);
        }
        catch (FormatException)
        {
            return format;
        }
    }

    public static Language ParseLanguage(string? value) =>
        string.Equals(value, "en", StringComparison.OrdinalIgnoreCase) ? Language.En : Language.Id;

    public static string ToCode(Language language) => language == Language.En ? "en" : "id";
}

public static class Translations
{
    public static readonly IReadOnlyDictionary<string, string> Id = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["app.title"] = "CircuitSync",
        ["app.subtitle"] = "Grouping circuit untuk Revit {0}",

        ["auth.heading"] = "Masuk",
        ["auth.email"] = "Email",
        ["auth.password"] = "Kata sandi",
        ["auth.sign_in"] = "Masuk",
        ["auth.sign_out"] = "Keluar",
        ["auth.use_code"] = "Masuk dengan kode email",
        ["auth.use_password"] = "Masuk dengan kata sandi",
        ["auth.send_code"] = "Kirim kode",
        ["auth.code"] = "Kode dari email",
        ["auth.verify"] = "Verifikasi",
        ["auth.code_sent"] = "Kode terkirim ke {0}",
        ["auth.signed_in_as"] = "Masuk sebagai {0}",
        ["auth.signed_out"] = "Sesi diakhiri",
        ["auth.failed"] = "Email atau kata sandi tidak cocok",
        ["auth.code_failed"] = "Kode tidak cocok atau sudah kedaluwarsa",
        ["auth.need_email"] = "Isi email dulu",

        ["project.heading"] = "Project",
        ["project.none"] = "Dokumen ini belum terikat ke project",
        ["project.name"] = "Nama project",
        ["project.create"] = "Buat project baru",
        ["project.link"] = "Hubungkan ke project",
        ["project.unlink"] = "Lepas ikatan",
        ["project.bound"] = "Terikat ke {0}",
        ["project.created"] = "Project {0} dibuat dan diikat ke dokumen ini",
        ["project.linked"] = "Dokumen diikat ke {0}",
        ["project.unlinked"] = "Ikatan project dilepas",
        ["project.need_name"] = "Isi nama project dulu",
        ["project.need_selection"] = "Pilih project dulu",
        ["project.reload"] = "Muat ulang daftar",

        ["action.heading"] = "Sinkronisasi",
        ["action.push"] = "Tarik model ke cloud",
        ["action.check"] = "Ambil rencana dari web",
        ["action.auto"] = "Ambil otomatis tiap {0} detik",
        ["action.place_tags"] = "Pasang tag nomor circuit di denah",
        ["action.web_url"] = "Alamat web app",
        ["action.web_url_hint"] = "Ambil dari kolom Domains project Anda di Vercel. Kosongkan untuk kembali ke bawaan.",
        ["action.open_web"] = "Buka web app",

        ["summary.devices"] = "{0} device",
        ["summary.panels"] = "{0} panel",
        ["summary.levels"] = "{0} level",
        ["summary.usable_panels"] = "{0} panel layak dipakai",

        ["status.unwired"] = "Belum di-circuit",
        ["status.no_panel"] = "Panel kosong",
        ["status.connected"] = "Tersambung",
        ["status.no_connector"] = "Tanpa connector",

        ["theme.light"] = "Terang",
        ["theme.dark"] = "Gelap",
        ["language.label"] = "Bahasa",

        ["log.heading"] = "Aktivitas",
        ["log.empty"] = "Belum ada aktivitas. Mulai dengan menarik model ke cloud.",
        ["log.push_start"] = "Membaca model…",
        ["log.push_done"] = "Model terkirim: {0} device, {1} panel",
        ["log.no_document"] = "Buka dokumen Revit dulu",
        ["log.not_signed_in"] = "Masuk dulu sebelum sinkronisasi",
        ["log.no_project"] = "Ikat dokumen ini ke project dulu",
        ["log.no_job"] = "Belum ada rencana di antrean",
        ["log.jobs_found"] = "{0} rencana di antrean, dikerjakan berurutan…",
        ["log.job_found"] = "Menerapkan {0} circuit…",
        ["log.applied"] = "{0} circuit diterapkan",
        ["log.applied_one"] = "{0} → {1}",
        ["log.rebuilt_one"] = "{0} dibangun ulang → {1}",
        ["log.apply_failed"] = "Circuit gagal diterapkan: {0}",
        ["log.job_error"] = "Rencana ditolak: {0}",
        ["log.network"] = "Cloud tidak bisa dihubungi. Periksa koneksi lalu coba lagi.",

        ["plan.empty_circuit"] = "Circuit tanpa device",
        ["plan.unknown_panel"] = "Panel sudah tidak ada di model",
        ["plan.panel_not_usable"] = "Panel {0} belum punya distribution system",
        ["plan.unknown_device"] = "Device sudah dihapus dari model",
        ["plan.device_without_connector"] = "Device tanpa connector listrik",
        ["plan.kind_mismatch"] = "Jenis device tidak cocok dengan circuit",
        ["plan.device_in_two_circuits"] = "Device dipakai di dua circuit",
        ["plan.already_connected"] = "Device sudah tersambung ke circuit lain",

        ["revit.command_tooltip"] = "Buka panel CircuitSync",
        ["revit.panel_name"] = "CircuitSync",
        ["revit.tab_name"] = "Electrical",
        ["revit.button_text"] = "Grouping\nCircuit",
    };

    public static readonly IReadOnlyDictionary<string, string> En = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["app.title"] = "CircuitSync",
        ["app.subtitle"] = "Circuit grouping for Revit {0}",

        ["auth.heading"] = "Sign in",
        ["auth.email"] = "Email",
        ["auth.password"] = "Password",
        ["auth.sign_in"] = "Sign in",
        ["auth.sign_out"] = "Sign out",
        ["auth.use_code"] = "Sign in with an email code",
        ["auth.use_password"] = "Sign in with a password",
        ["auth.send_code"] = "Send code",
        ["auth.code"] = "Code from email",
        ["auth.verify"] = "Verify",
        ["auth.code_sent"] = "Code sent to {0}",
        ["auth.signed_in_as"] = "Signed in as {0}",
        ["auth.signed_out"] = "Session ended",
        ["auth.failed"] = "That email and password do not match",
        ["auth.code_failed"] = "That code does not match or has expired",
        ["auth.need_email"] = "Enter an email first",

        ["project.heading"] = "Project",
        ["project.none"] = "This document is not bound to a project yet",
        ["project.name"] = "Project name",
        ["project.create"] = "Create project",
        ["project.link"] = "Link to project",
        ["project.unlink"] = "Remove binding",
        ["project.bound"] = "Bound to {0}",
        ["project.created"] = "Project {0} created and bound to this document",
        ["project.linked"] = "Document bound to {0}",
        ["project.unlinked"] = "Project binding removed",
        ["project.need_name"] = "Enter a project name first",
        ["project.need_selection"] = "Pick a project first",
        ["project.reload"] = "Reload list",

        ["action.heading"] = "Sync",
        ["action.push"] = "Push model to cloud",
        ["action.check"] = "Fetch plan from web",
        ["action.auto"] = "Fetch automatically every {0} seconds",
        ["action.place_tags"] = "Place circuit number tags on the plan",
        ["action.web_url"] = "Web app address",
        ["action.web_url_hint"] = "Copy it from the Domains panel of your Vercel project. Leave it empty to fall back to the built-in default.",
        ["action.open_web"] = "Open web app",

        ["summary.devices"] = "{0} devices",
        ["summary.panels"] = "{0} panels",
        ["summary.levels"] = "{0} levels",
        ["summary.usable_panels"] = "{0} panels usable",

        ["status.unwired"] = "Not circuited",
        ["status.no_panel"] = "No panel",
        ["status.connected"] = "Connected",
        ["status.no_connector"] = "No connector",

        ["theme.light"] = "Light",
        ["theme.dark"] = "Dark",
        ["language.label"] = "Language",

        ["log.heading"] = "Activity",
        ["log.empty"] = "Nothing yet. Start by pushing the model to the cloud.",
        ["log.push_start"] = "Reading the model…",
        ["log.push_done"] = "Model pushed: {0} devices, {1} panels",
        ["log.no_document"] = "Open a Revit document first",
        ["log.not_signed_in"] = "Sign in before syncing",
        ["log.no_project"] = "Bind this document to a project first",
        ["log.no_job"] = "Nothing in the queue",
        ["log.jobs_found"] = "{0} plans queued, applying them in order…",
        ["log.job_found"] = "Applying {0} circuits…",
        ["log.applied"] = "{0} circuits applied",
        ["log.applied_one"] = "{0} → {1}",
        ["log.rebuilt_one"] = "{0} rebuilt → {1}",
        ["log.apply_failed"] = "Circuit could not be applied: {0}",
        ["log.job_error"] = "Plan rejected: {0}",
        ["log.network"] = "The cloud is unreachable. Check the connection and try again.",

        ["plan.empty_circuit"] = "Circuit has no devices",
        ["plan.unknown_panel"] = "That panel no longer exists in the model",
        ["plan.panel_not_usable"] = "Panel {0} has no distribution system",
        ["plan.unknown_device"] = "That device was deleted from the model",
        ["plan.device_without_connector"] = "Device has no electrical connector",
        ["plan.kind_mismatch"] = "Device kind does not match the circuit",
        ["plan.device_in_two_circuits"] = "Device is used in two circuits",
        ["plan.already_connected"] = "Device is already on another circuit",

        ["revit.command_tooltip"] = "Open the CircuitSync panel",
        ["revit.panel_name"] = "CircuitSync",
        ["revit.tab_name"] = "Electrical",
        ["revit.button_text"] = "Circuit\nGrouping",
    };
}
