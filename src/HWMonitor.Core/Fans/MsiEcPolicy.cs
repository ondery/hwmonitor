// Copyright (c) 2026 Ondery
// SPDX-License-Identifier: MIT

namespace HWMonitor.Core.Fans;

public enum MsiEcDecisionKind
{
    Disabled,
    NotListed,
    DutyUnsupported,
}

public sealed record MsiEcDecision(MsiEcDecisionKind Kind, string Detail);

/// <summary>
/// Decides whether an experimental MSI embedded-controller path may run.
/// This policy never produces register writes. Windows port access races with
/// firmware, and the published map for the Creator Z16 generation does not
/// include a constant fan-duty register.
/// </summary>
public static class MsiEcPolicy
{
    public const string UnsupportedMessage = "Fan control is not supported on this device";

    public static MsiEcDecision Evaluate(bool experimentalEnabled, string? productName)
    {
        if (!experimentalEnabled)
        {
            return new MsiEcDecision(
                MsiEcDecisionKind.Disabled,
                "Experimental MSI embedded-controller fan control is off.");
        }

        if (IsCreatorZ16A11Uet(productName))
        {
            return new MsiEcDecision(
                MsiEcDecisionKind.NotListed,
                "MSI Creator Z16 A11UET is not on the experimental allow-list. The related Creator Z16 A11UE firmware uses the Linux msi-ec CONF14 layout, whose constant fan-duty register is marked unsupported. No embedded-controller bytes are written.");
        }

        if (IsCreatorZ16A11Ue(productName))
        {
            return new MsiEcDecision(
                MsiEcDecisionKind.DutyUnsupported,
                "Creator Z16 A11UE is documented for fan profiles only. CONF14 does not publish a constant duty register, so percentage control is not applied.");
        }

        return new MsiEcDecision(
            MsiEcDecisionKind.NotListed,
            "This model is not on the experimental MSI embedded-controller allow-list. No embedded-controller bytes are written.");
    }

    public static bool IsCreatorZ16A11Uet(string? productName) =>
        Contains(productName, "A11UET");

    public static bool IsCreatorZ16A11Ue(string? productName) =>
        Contains(productName, "A11UE") && !Contains(productName, "A11UET");

    private static bool Contains(string? productName, string token) =>
        !string.IsNullOrWhiteSpace(productName)
        && productName.Contains(token, StringComparison.OrdinalIgnoreCase);
}
