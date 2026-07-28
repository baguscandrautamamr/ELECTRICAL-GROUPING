using Autodesk.Revit.DB;

namespace CircuitSync.Revit;

/// <summary>
/// Label nomor circuit di samping device. Selalu <see cref="IndependentTag"/> yang
/// membaca parameter <c>Circuit Number</c> — bukan TextNote, karena TextNote
/// menghasilkan data mati yang tidak ikut berubah saat circuit diubah.
/// </summary>
internal static class TagPlacer
{
    /// <summary>
    /// Menempatkan tag di view aktif untuk setiap elemen yang belum punya tag.
    /// Mengembalikan jumlah tag yang benar-benar dibuat; nol bukan kegagalan —
    /// view bisa bukan denah, atau family tag-nya belum di-load.
    /// </summary>
    public static int Place(Document doc, IReadOnlyList<Element> elements, double offsetMm)
    {
        var view = doc.ActiveView;
        if (view is null || !CanHostTags(view))
        {
            return 0;
        }

        var tagged = ExistingTagOwners(doc, view.Id);
        var offset = Units.FromMillimeters(offsetMm);
        var placed = 0;

        foreach (var element in elements)
        {
            if (tagged.Contains(element.Id))
            {
                continue;
            }

            var symbolId = TagSymbolFor(doc, element);
            if (symbolId == ElementId.InvalidElementId)
            {
                continue;
            }

            var origin = (element.Location as LocationPoint)?.Point;
            if (origin is null)
            {
                continue;
            }

            var head = new XYZ(origin.X + offset, origin.Y + offset, origin.Z);

            IndependentTag.Create(doc, symbolId, view.Id, new Reference(element),
                addLeader: false, TagOrientation.Horizontal, head);
            placed++;
        }

        return placed;
    }

    /// <summary>
    /// Tag hanya masuk akal di view yang punya denah. View 3D, schedule, dan sheet
    /// dilewati tanpa pesan error.
    /// </summary>
    private static bool CanHostTags(View view) =>
        view is ViewPlan && !view.IsTemplate;

    private static HashSet<ElementId> ExistingTagOwners(Document doc, ElementId viewId)
    {
        var owners = new HashSet<ElementId>();

        var tags = new FilteredElementCollector(doc, viewId)
            .OfClass(typeof(IndependentTag))
            .Cast<IndependentTag>();

        foreach (var tag in tags)
        {
            foreach (var id in tag.GetTaggedLocalElementIds())
            {
                owners.Add(id);
            }
        }

        return owners;
    }

    /// <summary>
    /// Tag type pertama yang kategorinya cocok dengan kategori elemen. Family tag-nya
    /// harus sudah di-load di project dan label-nya menunjuk <c>Circuit Number</c> —
    /// itu urusan template, bukan sesuatu yang bisa kita buat dari API.
    /// </summary>
    private static ElementId TagSymbolFor(Document doc, Element element)
    {
        var tagCategory = element.Category?.BuiltInCategory switch
        {
            BuiltInCategory.OST_LightingFixtures => BuiltInCategory.OST_LightingFixtureTags,
            BuiltInCategory.OST_ElectricalFixtures => BuiltInCategory.OST_ElectricalFixtureTags,
            _ => (BuiltInCategory?)null,
        };

        if (tagCategory is null)
        {
            return ElementId.InvalidElementId;
        }

        var symbol = new FilteredElementCollector(doc)
            .OfCategory(tagCategory.Value)
            .WhereElementIsElementType()
            .OfClass(typeof(FamilySymbol))
            .FirstElement();

        return symbol?.Id ?? ElementId.InvalidElementId;
    }
}
