using Autodesk.Revit.DB;

namespace CircuitSync.Revit;

/// <summary>
/// Konversi dari satuan internal Revit (feet, dan turunannya) ke satuan yang dipakai
/// kontrak data. Semua konversi lewat sini supaya tidak ada angka 304.8 yang
/// berkeliaran di kode.
/// </summary>
public static class Units
{
    public static double ToMillimeters(double internalValue) =>
        UnitUtils.ConvertFromInternalUnits(internalValue, UnitTypeId.Millimeters);

    public static double ToMillimetersRounded(double internalValue) =>
        Math.Round(ToMillimeters(internalValue), 1);

    public static double FromMillimeters(double millimeters) =>
        UnitUtils.ConvertToInternalUnits(millimeters, UnitTypeId.Millimeters);

    public static double ToVoltAmperes(double internalValue) =>
        UnitUtils.ConvertFromInternalUnits(internalValue, UnitTypeId.VoltAmperes);
}
