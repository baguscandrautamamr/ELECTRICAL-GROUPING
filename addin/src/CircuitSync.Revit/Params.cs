using Autodesk.Revit.DB;

namespace CircuitSync.Revit;

/// <summary>
/// Pembacaan parameter yang tidak melempar. Parameter bisa tidak ada, kosong, atau
/// bertipe lain dari yang diduga tergantung family — dan itu bukan alasan gagal
/// menarik seluruh model.
/// </summary>
internal static class Params
{
    public static string? String(Element element, BuiltInParameter id)
    {
        var parameter = element.get_Parameter(id);
        if (parameter is null || !parameter.HasValue)
        {
            return null;
        }

        var value = parameter.StorageType switch
        {
            StorageType.String => parameter.AsString(),
            StorageType.Integer or StorageType.Double => parameter.AsValueString(),
            StorageType.ElementId => parameter.AsValueString(),
            _ => null,
        };

        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public static double? Double(Element element, BuiltInParameter id)
    {
        var parameter = element.get_Parameter(id);
        return parameter is { HasValue: true, StorageType: StorageType.Double }
            ? parameter.AsDouble()
            : null;
    }

    public static int? Int(Element element, BuiltInParameter id)
    {
        var parameter = element.get_Parameter(id);
        return parameter is { HasValue: true, StorageType: StorageType.Integer }
            ? parameter.AsInteger()
            : null;
    }

    /// <summary>
    /// Parameter yang hidup di type, bukan di instance — misal <c>Circuit Prefix</c>
    /// dan jumlah slot panel.
    /// </summary>
    public static string? TypeString(Document doc, Element instance, BuiltInParameter id)
    {
        var type = doc.GetElement(instance.GetTypeId());
        return type is null ? null : String(type, id);
    }

    public static int? TypeInt(Document doc, Element instance, BuiltInParameter id)
    {
        var type = doc.GetElement(instance.GetTypeId());
        return type is null ? null : Int(type, id);
    }
}
