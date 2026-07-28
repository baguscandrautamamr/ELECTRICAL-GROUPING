using System.Reflection;

// Verifikasi anggota Revit API tanpa membuka Revit. Hasilnya dicatat ke
// docs/api-verified.md — lihat file itu untuk contoh pemakaian.
//
//   dotnet run --project tools/ApiProbe -- type ElectricalSystem
//   dotnet run --project tools/ApiProbe -- enum BuiltInParameter RBS_ELEC PREFIX
//   dotnet run --project tools/ApiProbe -- find IndependentTag
//   dotnet run --project tools/ApiProbe -- namespaces

if (args.Length == 0)
{
    Console.Error.WriteLine("pakai: ApiProbe <type|enum|find|namespaces> [nama] [kata kunci...]");
    return 1;
}

var refListPath = Path.Combine(AppContext.BaseDirectory, "revit-refs.txt");
if (!File.Exists(refListPath))
{
    Console.Error.WriteLine($"tidak ada {refListPath}. Jalankan `dotnet build` dulu.");
    return 1;
}

var revitDlls = File.ReadAllLines(refListPath)
    .Select(line => line.Trim())
    .Where(line => line.Length > 0 && File.Exists(line))
    .ToList();

if (revitDlls.Count == 0)
{
    Console.Error.WriteLine("reference assembly Revit tidak ditemukan. Coba `dotnet restore`.");
    return 1;
}

// MetadataLoadContext butuh melihat BCL juga, kalau tidak base type tidak bisa dibaca.
var resolverPaths = revitDlls
    .Concat(Directory.GetFiles(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "*.dll"))
    .ToList();

using var context = new MetadataLoadContext(new PathAssemblyResolver(resolverPaths));

var types = revitDlls
    .Select(context.LoadFromAssemblyPath)
    .SelectMany(SafeTypes)
    .Where(type => type.FullName?.StartsWith("Autodesk.", StringComparison.Ordinal) == true)
    .ToList();

var mode = args[0];
var name = args.Length > 1 ? args[1] : "";
var keywords = args.Skip(2).ToArray();

switch (mode)
{
    case "namespaces":
        foreach (var group in types.GroupBy(t => t.Namespace).OrderByDescending(g => g.Count()))
        {
            Console.WriteLine($"{group.Count(),6}  {group.Key}");
        }

        break;

    case "find":
        foreach (var type in types
                     .Where(t => t.Name.Contains(name, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            Console.WriteLine(type.FullName);
        }

        break;

    case "enum":
        foreach (var type in types.Where(t => t.Name == name))
        {
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static)
                         .Select(f => f.Name)
                         .Where(f => keywords.All(k => f.Contains(k, StringComparison.OrdinalIgnoreCase)))
                         .OrderBy(f => f, StringComparer.Ordinal))
            {
                Console.WriteLine(field);
            }
        }

        break;

    case "type":
        foreach (var type in types.Where(t => t.Name == name || t.FullName == name))
        {
            Console.WriteLine($"### {type.FullName} : {type.BaseType?.Name ?? "-"}");
            foreach (var line in Describe(type)
                         .Where(l => keywords.All(k => l.Contains(k, StringComparison.OrdinalIgnoreCase)))
                         .OrderBy(l => l, StringComparer.Ordinal))
            {
                Console.WriteLine(line);
            }
        }

        break;

    default:
        Console.Error.WriteLine($"mode tidak dikenal: {mode}");
        return 1;
}

return 0;

static IEnumerable<Type> SafeTypes(Assembly assembly)
{
    try
    {
        return assembly.GetTypes();
    }
    catch (ReflectionTypeLoadException ex)
    {
        return ex.Types.Where(t => t is not null)!;
    }
}

static IEnumerable<string> Describe(Type type)
{
    const BindingFlags Flags = BindingFlags.Public | BindingFlags.Instance |
                               BindingFlags.Static | BindingFlags.DeclaredOnly;

    foreach (var member in type.GetMembers(Flags))
    {
        switch (member)
        {
            case MethodInfo { IsSpecialName: false } method:
                yield return $"  M {method.ReturnType.Name} {method.Name}({Args(method.GetParameters())})";
                break;
            case PropertyInfo property:
                yield return $"  P {property.PropertyType.Name} {property.Name} " +
                             (property.CanWrite ? "{get;set;}" : "{get;}");
                break;
            case ConstructorInfo constructor:
                yield return $"  C ctor({Args(constructor.GetParameters())})";
                break;
        }
    }

    static string Args(ParameterInfo[] parameters) =>
        string.Join(", ", parameters.Select(p => $"{p.ParameterType.Name} {p.Name}"));
}
