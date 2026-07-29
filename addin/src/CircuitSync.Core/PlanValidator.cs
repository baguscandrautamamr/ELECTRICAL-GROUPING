namespace CircuitSync.Core;

/// <summary>
/// Satu masalah pada rencana. <see cref="MessageKey"/> diterjemahkan di lapisan UI —
/// Core tidak pernah menghasilkan teks yang siap dibaca user.
/// </summary>
public sealed record PlanProblem(Guid CircuitId, string MessageKey, string Detail);

/// <summary>
/// Memeriksa rencana dari web terhadap kondisi model sekarang, sebelum satu pun
/// transaksi Revit dibuka. Model bisa sudah berubah sejak snapshot terakhir.
/// </summary>
public static class PlanValidator
{
    public const string EmptyCircuit = "plan.empty_circuit";
    public const string UnknownPanel = "plan.unknown_panel";
    public const string PanelNotUsable = "plan.panel_not_usable";
    public const string UnknownDevice = "plan.unknown_device";
    public const string DeviceWithoutConnector = "plan.device_without_connector";
    public const string KindMismatch = "plan.kind_mismatch";
    public const string DeviceInTwoCircuits = "plan.device_in_two_circuits";
    public const string AlreadyConnected = "plan.already_connected";

    /// <summary>
    /// Mengembalikan circuit yang layak diterapkan, beserta daftar masalah.
    /// Satu circuit bermasalah tidak menggugurkan yang lain — itu sengaja, sama
    /// seperti pembungkusan SubTransaction per circuit saat apply.
    /// </summary>
    public static PlanValidation Validate(IReadOnlyList<CircuitRow> circuits, ModelSnapshot snapshot)
    {
        var problems = new List<PlanProblem>();
        var accepted = new List<CircuitRow>();

        var panels = snapshot.Panels.ToDictionary(p => p.RevitUniqueId, StringComparer.Ordinal);
        var devices = snapshot.Devices.ToDictionary(d => d.RevitUniqueId, StringComparer.Ordinal);
        var claimedBy = new Dictionary<string, Guid>(StringComparer.Ordinal);

        foreach (var circuit in circuits)
        {
            var circuitProblems = new List<PlanProblem>();

            // Circuit yang sudah pernah diterapkan membawa UniqueId ElectricalSystem-nya.
            // Kalau isinya diubah dari web, device di dalamnya tentu berstatus `connected` —
            // ke circuit ini sendiri. Menolaknya membuat circuit yang sudah jalan tidak
            // pernah bisa diubah lagi.
            var rebuilding = string.IsNullOrEmpty(circuit.RevitUniqueId) ? null : circuit.RevitUniqueId;

            if (circuit.DeviceUniqueIds.Length == 0)
            {
                circuitProblems.Add(new PlanProblem(circuit.Id, EmptyCircuit, ""));
            }

            if (!panels.TryGetValue(circuit.PanelUniqueId, out var panel))
            {
                circuitProblems.Add(new PlanProblem(circuit.Id, UnknownPanel, circuit.PanelUniqueId));
            }
            else if (!panel.IsUsable)
            {
                circuitProblems.Add(new PlanProblem(circuit.Id, PanelNotUsable, panel.Name));
            }

            foreach (var deviceId in circuit.DeviceUniqueIds)
            {
                if (!devices.TryGetValue(deviceId, out var device))
                {
                    circuitProblems.Add(new PlanProblem(circuit.Id, UnknownDevice, deviceId));
                    continue;
                }

                if (device.Status == DeviceStatus.NoConnector)
                {
                    circuitProblems.Add(new PlanProblem(circuit.Id, DeviceWithoutConnector, deviceId));
                }

                if (device.Status == DeviceStatus.Connected && !IsMemberOf(snapshot, deviceId, rebuilding))
                {
                    circuitProblems.Add(new PlanProblem(circuit.Id, AlreadyConnected, deviceId));
                }

                if (device.Kind != circuit.Kind)
                {
                    circuitProblems.Add(new PlanProblem(circuit.Id, KindMismatch, deviceId));
                }

                if (claimedBy.TryGetValue(deviceId, out var owner) && owner != circuit.Id)
                {
                    circuitProblems.Add(new PlanProblem(circuit.Id, DeviceInTwoCircuits, deviceId));
                }
            }

            if (circuitProblems.Count == 0)
            {
                accepted.Add(circuit);
                foreach (var deviceId in circuit.DeviceUniqueIds)
                {
                    claimedBy[deviceId] = circuit.Id;
                }
            }
            else
            {
                problems.AddRange(circuitProblems);
            }
        }

        return new PlanValidation(accepted, problems);
    }

    /// <summary>
    /// Benar kalau device ini memang anggota ElectricalSystem yang sedang dibangun ulang.
    /// </summary>
    private static bool IsMemberOf(ModelSnapshot snapshot, string deviceId, string? systemUniqueId) =>
        systemUniqueId is not null &&
        snapshot.DeviceSystems.TryGetValue(deviceId, out var owner) &&
        string.Equals(owner, systemUniqueId, StringComparison.Ordinal);
}

public sealed record PlanValidation(IReadOnlyList<CircuitRow> Accepted, IReadOnlyList<PlanProblem> Problems)
{
    public bool AllAccepted => Problems.Count == 0;
}
