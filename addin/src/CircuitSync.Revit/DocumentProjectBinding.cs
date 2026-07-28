using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;

namespace CircuitSync.Revit;

/// <summary>
/// Satu GUID project Supabase per dokumen, disimpan di <c>ProjectInformation</c> lewat
/// Extensible Storage. Ini satu-satunya sumber kebenaran tentang "model ini milik
/// project Supabase yang mana" — bukan file setting di samping, bukan nama dokumen.
/// </summary>
public static class DocumentProjectBinding
{
    /// <summary>
    /// Jangan pernah diubah. Mengubahnya membuat semua dokumen yang sudah terikat
    /// kelihatan belum terikat.
    /// </summary>
    private static readonly Guid SchemaGuid = new("6f1d0e2a-9c4b-4f18-9a1c-7e2f5b3d8a41");

    private const string SchemaName = "CircuitSyncBinding";
    private const string FieldProjectId = "ProjectId";

    /// <summary>Harus sama dengan VendorId di CircuitSync.addin, kalau tidak tulis akan ditolak.</summary>
    public const string VendorId = "CSYN";

    public static Guid? Read(Document doc)
    {
        var schema = Schema.Lookup(SchemaGuid);
        if (schema is null || !schema.ReadAccessGranted())
        {
            return null;
        }

        var entity = doc.ProjectInformation.GetEntity(schema);
        if (entity is null || !entity.IsValid())
        {
            return null;
        }

        var raw = entity.Get<string>(FieldProjectId);
        return Guid.TryParse(raw, out var projectId) && projectId != Guid.Empty ? projectId : null;
    }

    /// <summary>Harus dipanggil di dalam transaksi yang sudah dibuka pemanggil.</summary>
    public static void Write(Document doc, Guid projectId)
    {
        var schema = Schema.Lookup(SchemaGuid) ?? Build();
        var entity = new Entity(schema);
        entity.Set(FieldProjectId, projectId.ToString());
        doc.ProjectInformation.SetEntity(entity);
    }

    /// <summary>Harus dipanggil di dalam transaksi yang sudah dibuka pemanggil.</summary>
    public static void Clear(Document doc)
    {
        var schema = Schema.Lookup(SchemaGuid);
        if (schema is not null)
        {
            doc.ProjectInformation.DeleteEntity(schema);
        }
    }

    private static Schema Build()
    {
        using var builder = new SchemaBuilder(SchemaGuid);
        builder.SetSchemaName(SchemaName);
        builder.SetDocumentation("Mengikat dokumen Revit ke satu project CircuitSync di Supabase.");
        builder.SetVendorId(VendorId);
        builder.SetReadAccessLevel(AccessLevel.Public);
        builder.SetWriteAccessLevel(AccessLevel.Vendor);
        builder.AddSimpleField(FieldProjectId, typeof(string));
        return builder.Finish();
    }
}
