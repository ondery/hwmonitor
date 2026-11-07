// Copyright (c) 2026 Ondery
// SPDX-License-Identifier: MIT

using System.Management;
using System.Text.Json;
using HWMonitor.Core.Fans;
using HWMonitor.Core.Text;

namespace HWMonitor;

internal sealed class FanControlService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly HardwareSession _hardware;
    private readonly string _path;
    private readonly MsiEcFanBackend _msi = new();

    public FanControlService(HardwareSession hardware, string directory)
    {
        _hardware = hardware;
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "fan-control.json");
        var stored = ReadFile();
        ModeIndex = stored.ModeIndex;
        SoftwareControl = stored.SoftwareControl;
    }

    public bool SoftwareControl { get; private set; }

    public int ModeIndex { get; private set; } = -1;

    public int AppliedFans { get; private set; }

    public string? Detail { get; private set; }

    public bool Supported { get; private set; } = true;

    public void RecoverOnStartup()
    {
        if (!SoftwareControl)
        {
            return;
        }

        _hardware.RestoreFans();
        _msi.RestoreFirmware();
        SoftwareControl = false;
        ModeIndex = -1;
        WriteFile();
    }

    public void RestoreToAuto()
    {
        _hardware.RestoreFans();
        _msi.RestoreFirmware();
        SoftwareControl = false;
        ModeIndex = -1;
        AppliedFans = 0;
        Detail = null;
        WriteFile();
    }

    public void Observe(bool experimentalMsi)
    {
        if (SoftwareControl)
        {
            Supported = true;
            return;
        }

        if (_hardware.ControlCount > 0)
        {
            Supported = true;
            Detail = null;
            return;
        }

        Supported = false;
        ModeIndex = -1;
        var decision = _msi.Explain(experimentalMsi);
        Detail = experimentalMsi
            ? $"{UiText.FanUnsupported} {decision.Detail}"
            : UiText.FanUnsupported;
    }

    public void Cycle(FanModeSet modes, bool experimentalMsi)
    {
        var controls = _hardware.ControlCount;
        if (controls == 0)
        {
            _hardware.Read();
            controls = _hardware.ControlCount;
        }

        if (controls == 0)
        {
            Supported = false;
            ModeIndex = -1;
            SoftwareControl = false;
            var decision = _msi.Explain(experimentalMsi);
            Detail = experimentalMsi
                ? $"{UiText.FanUnsupported} {decision.Detail}"
                : UiText.FanUnsupported;
            return;
        }

        Supported = true;
        var next = FanModeSet.Advance(ModeIndex, modes);
        if (next < 0)
        {
            RestoreToAuto();
            return;
        }

        SoftwareControl = true;
        ModeIndex = next;
        WriteFile();
        var duty = modes.Modes[next].DutyPercent;
        AppliedFans = _hardware.SetFanDuty(duty);
        if (AppliedFans == 0)
        {
            RestoreToAuto();
            Supported = false;
            Detail = UiText.FanApplyFailed;
            return;
        }

        Detail = null;
    }

    private StoredFan ReadFile()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return new StoredFan();
            }

            return JsonSerializer.Deserialize<StoredFan>(File.ReadAllText(_path)) ?? new StoredFan();
        }
        catch (Exception ex)
        {
            ExtensionLog.Write(ex.Message);
            return new StoredFan();
        }
    }

    private void WriteFile()
    {
        var stored = new StoredFan { SoftwareControl = SoftwareControl, ModeIndex = ModeIndex };
        File.WriteAllText(_path, JsonSerializer.Serialize(stored, JsonOptions));
    }

    private sealed class StoredFan
    {
        public bool SoftwareControl { get; set; }

        public int ModeIndex { get; set; } = -1;
    }
}

/// <summary>
/// Separate experimental MSI path. It identifies the product and consults the
/// documented allow-list. It does not write embedded-controller registers.
/// </summary>
internal sealed class MsiEcFanBackend
{
    private string? _product;

    public MsiEcDecision Explain(bool experimentalEnabled)
    {
        _product ??= ReadProduct();
        return MsiEcPolicy.Evaluate(experimentalEnabled, _product);
    }

    public void RestoreFirmware()
    {
        // No controller bytes were written, so there is nothing to roll back here.
        // LibreHardwareMonitor controls are restored by the caller.
    }

    private static string? ReadProduct()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Model FROM Win32_ComputerSystem");
            foreach (var item in searcher.Get())
            {
                return item["Model"]?.ToString();
            }
        }
        catch (Exception ex)
        {
            ExtensionLog.Write(ex.Message);
        }

        return null;
    }
}
