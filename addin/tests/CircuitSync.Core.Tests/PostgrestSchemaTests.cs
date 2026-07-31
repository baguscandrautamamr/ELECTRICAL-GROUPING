using System.Text.Json;
using System.Text.Json.Nodes;
using CircuitSync.Core;
using Xunit;

namespace CircuitSync.Core.Tests;

/// <summary>
/// Add-in dan database dipasang terpisah, jadi add-in yang lebih baru daripada
/// database adalah keadaan biasa — bukan kemungkinan jauh. Perilaku di sini yang
/// menentukan apakah selisih itu berakhir sebagai satu fitur yang belum jalan, atau
/// sebagai seluruh tarikan model yang gagal.
/// </summary>
public class PostgrestSchemaTests
{
    private const string Rejected =
        """{"code":"PGRST204","details":null,"hint":null,"message":"Could not find the 'panel_unique_id' column of 'devices' in the schema cache"}""";

    [Fact]
    public void Reads_the_column_name_postgrest_complains_about()
    {
        Assert.Equal("panel_unique_id", PostgrestSchema.UnknownColumn(Rejected));
    }

    /// <summary>
    /// Kegagalan lain tidak boleh terbaca sebagai kolom. Membuang kolom karena salah baca
    /// menghilangkan data diam-diam; menyerah hanya menghasilkan pesan.
    /// </summary>
    [Theory]
    [InlineData("""{"code":"PGRST102","message":"All object keys must match"}""")]
    [InlineData("""{"code":"23505","message":"duplicate key value violates unique constraint 'devices_pkey'"}""")]
    [InlineData("""{"message":"Could not find the 'x' column of 'y' in the schema cache"}""")]
    [InlineData("")]
    [InlineData(null)]
    public void Leaves_every_other_failure_alone(string? body)
    {
        Assert.Null(PostgrestSchema.UnknownColumn(body));
    }

    /// <summary>
    /// Tabel yang belum ada adalah kasus yang lebih sering daripada kolom yang belum ada:
    /// setiap fitur baru datang bersama tabelnya sendiri. Sebelum ini kegagalannya
    /// menggagalkan seluruh tarikan model dengan <c>http_404</c>, termasuk device dan
    /// panel yang sudah lama ada.
    /// </summary>
    [Theory]
    [InlineData(
        """{"code":"PGRST205","details":null,"hint":null,"message":"Could not find the table 'public.line_styles' in the schema cache"}""",
        "line_styles")]
    [InlineData(
        """{"code":"PGRST205","message":"Could not find the table 'layout_lighting_devices' in the schema cache"}""",
        "layout_lighting_devices")]
    [InlineData(
        """{"code":"42P01","message":"relation \"public.line_styles\" does not exist"}""",
        "line_styles")]
    public void Reads_the_table_name_postgrest_complains_about(string body, string expected)
    {
        Assert.Equal(expected, PostgrestSchema.MissingTable(body));
    }

    /// <summary>
    /// Kegagalan lain tidak boleh terbaca sebagai tabel yang hilang. Melewati sebuah tabel
    /// karena salah baca membuat fiturnya mati diam-diam; menyerah menghasilkan pesan.
    /// </summary>
    [Theory]
    [InlineData("""{"code":"PGRST204","message":"Could not find the 'panel_unique_id' column of 'devices' in the schema cache"}""")]
    [InlineData("""{"code":"PGRST301","message":"JWT expired"}""")]
    [InlineData("""{"message":"Could not find the table 'public.line_styles' in the schema cache"}""")]
    [InlineData("""{"code":"PGRST205","message":"sesuatu yang lain"}""")]
    [InlineData("")]
    [InlineData(null)]
    public void Leaves_every_other_failure_alone_for_tables(string? body)
    {
        Assert.Null(PostgrestSchema.MissingTable(body));
    }

    /// <summary>
    /// Dua penanda yang tidak boleh saling tertukar: kolom yang hilang membuat sebagian
    /// isi tabel kosong, tabel yang hilang membuat seluruh fiturnya tidak ada. Yang satu
    /// dijawab dengan membuang kolom lalu mengulang, yang lain dengan melewati tabelnya.
    /// </summary>
    [Fact]
    public void Column_and_table_failures_never_read_as_each_other()
    {
        const string missingTable =
            """{"code":"PGRST205","message":"Could not find the table 'public.line_styles' in the schema cache"}""";

        Assert.Null(PostgrestSchema.MissingTable(Rejected));
        Assert.Null(PostgrestSchema.UnknownColumn(missingTable));
    }

    [Fact]
    public void Drops_the_column_from_every_row_of_a_bulk_insert()
    {
        var rows = JsonNode.Parse(
            """
            [
              {"revit_unique_id":"a","x_mm":1,"panel_unique_id":"p1"},
              {"revit_unique_id":"b","x_mm":2,"panel_unique_id":null}
            ]
            """);

        Assert.True(PostgrestSchema.Strip(rows, "panel_unique_id"));

        // Semua baris, bukan yang pertama saja: PostgREST menolak bulk insert yang
        // objeknya tidak sekunci, jadi menyisakannya di satu baris menukar satu
        // kegagalan dengan kegagalan lain.
        Assert.Equal(
            """[{"revit_unique_id":"a","x_mm":1},{"revit_unique_id":"b","x_mm":2}]""",
            rows!.ToJsonString());
    }

    [Fact]
    public void Drops_the_column_from_a_single_object_patch()
    {
        var patch = JsonNode.Parse("""{"status":"connected","panel_unique_id":"p1"}""");

        Assert.True(PostgrestSchema.Strip(patch, "panel_unique_id"));
        Assert.Equal("""{"status":"connected"}""", patch!.ToJsonString());
    }

    /// <summary>
    /// Kolom yang memang tidak ada di payload berarti pesannya bicara tentang hal lain.
    /// Mengulang permintaan yang sama persis hanya menghasilkan kegagalan yang sama, dan
    /// tanpa penjaga ini pengulangannya tidak pernah berhenti.
    /// </summary>
    [Fact]
    public void Refuses_when_the_column_is_not_in_the_payload()
    {
        var rows = JsonNode.Parse("""[{"revit_unique_id":"a"}]""");

        Assert.False(PostgrestSchema.Strip(rows, "panel_unique_id"));
        Assert.False(PostgrestSchema.Strip(JsonNode.Parse("42"), "panel_unique_id"));
        Assert.False(PostgrestSchema.Strip(null, "panel_unique_id"));
    }

    /// <summary>
    /// Baris device yang kehilangan kolom panel tetap membawa geometrinya. Kalau suatu
    /// saat pembuangan itu ikut menyapu <c>x_mm</c>, denah di web berubah jadi setumpuk
    /// titik di pojok — dan gejalanya muncul jauh dari sebabnya.
    /// </summary>
    [Fact]
    public void Keeps_the_rest_of_the_device_row_intact()
    {
        var row = JsonSerializer.SerializeToNode(
            new DeviceRow
            {
                RevitUniqueId = "abc",
                LevelKey = "L1",
                FamilyKey = "Downlight::18W",
                XMm = 1000,
                YMm = 2000,
                Status = DeviceStatus.Connected,
                PanelUniqueId = "panel-1",
            },
            CircuitSyncJson.Options);

        Assert.True(PostgrestSchema.Strip(row, "panel_unique_id"));

        var kept = Assert.IsType<JsonObject>(row);
        Assert.False(kept.ContainsKey("panel_unique_id"));
        Assert.Equal("1000", kept["x_mm"]!.ToJsonString());
        Assert.Equal("2000", kept["y_mm"]!.ToJsonString());
        Assert.Equal("\"L1\"", kept["level_key"]!.ToJsonString());
        Assert.Equal("\"Downlight::18W\"", kept["family_key"]!.ToJsonString());
    }
}
