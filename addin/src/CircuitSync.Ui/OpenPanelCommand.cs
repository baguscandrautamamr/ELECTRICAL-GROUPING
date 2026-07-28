using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;

namespace CircuitSync.Ui;

/// <summary>
/// Satu-satunya command add-in. Isinya sengaja tipis: hanya membuka panel.
/// <see cref="TransactionMode.Manual"/> karena semua transaksi dibuka sendiri di
/// lapisan Revit, bukan oleh Revit atas nama kita.
/// </summary>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class OpenPanelCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, Autodesk.Revit.DB.ElementSet elements)
    {
        try
        {
            CircuitSyncApp.ShowPanel(commandData.Application);
            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            return Result.Failed;
        }
    }
}
