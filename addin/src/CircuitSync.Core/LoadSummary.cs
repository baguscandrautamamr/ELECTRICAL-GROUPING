namespace CircuitSync.Core;

/// <summary>
/// Ringkasan beban satu circuit. Hanya menghitung, tidak memutuskan — keputusan
/// jumlah device per circuit ada di tangan engineer di web.
/// </summary>
public sealed record LoadSummary(int DeviceCount, double TotalVa, int MissingVaCount)
{
    /// <summary>
    /// Arus perkiraan. Nol kalau tegangan tidak diketahui, bukan pembagian dengan nol.
    /// </summary>
    public double AmpsAt(double volts) => volts <= 0 ? 0 : TotalVa / volts;

    public static LoadSummary For(IEnumerable<DeviceRow> devices)
    {
        var count = 0;
        var total = 0d;
        var missing = 0;

        foreach (var device in devices)
        {
            count++;
            if (device.Va is { } va && va > 0)
            {
                total += va;
            }
            else
            {
                missing++;
            }
        }

        return new LoadSummary(count, total, missing);
    }

    public static LoadSummary For(CircuitRow circuit, ModelSnapshot snapshot)
    {
        var byId = snapshot.Devices.ToDictionary(d => d.RevitUniqueId, StringComparer.Ordinal);
        return For(circuit.DeviceUniqueIds
            .Select(id => byId.TryGetValue(id, out var device) ? device : null)
            .Where(d => d is not null)
            .Select(d => d!));
    }
}
