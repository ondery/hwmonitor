// Copyright (c) 2026 Ondery
// SPDX-License-Identifier: MIT

using HWMonitor.Core.Sensors;
using LibreHardwareMonitor.Hardware;

namespace HWMonitor;

internal sealed class HardwareSession : IDisposable
{
    private readonly Computer _computer = new()
    {
        IsCpuEnabled = true,
        IsGpuEnabled = true,
        IsMotherboardEnabled = true,
        IsControllerEnabled = true,
    };

    private readonly object _gate = new();
    private readonly Dictionary<string, IControl> _controls = new(StringComparer.Ordinal);
    private bool _openAttempted;
    private bool _open;
    private string? _openError;

    public string? OpenError
    {
        get
        {
            lock (_gate)
            {
                return _openError;
            }
        }
    }

    public int ControlCount
    {
        get
        {
            lock (_gate)
            {
                return _controls.Count;
            }
        }
    }

    public IReadOnlyList<RawSensor> Read()
    {
        lock (_gate)
        {
            return ReadCore();
        }
    }

    public int SetFanDuty(float percent)
    {
        lock (_gate)
        {
            if (_controls.Count == 0)
            {
                ReadCore();
            }

            var applied = 0;
            foreach (var control in _controls.Values)
            {
                try
                {
                    control.SetSoftware(ScaleDuty(control, percent));
                    applied++;
                }
                catch (Exception ex)
                {
                    ExtensionLog.Write(ex.Message);
                }
            }

            return applied;
        }
    }

    public int RestoreFans()
    {
        lock (_gate)
        {
            if (_controls.Count == 0)
            {
                ReadCore();
            }

            var restored = 0;
            foreach (var control in _controls.Values)
            {
                try
                {
                    control.SetDefault();
                    restored++;
                }
                catch (Exception ex)
                {
                    ExtensionLog.Write(ex.Message);
                }
            }

            return restored;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_open)
            {
                try
                {
                    _computer.Close();
                }
                catch (Exception ex)
                {
                    ExtensionLog.Write(ex.Message);
                }
            }

            _open = false;
        }
    }

    private IReadOnlyList<RawSensor> ReadCore()
    {
        EnsureOpen();
        _controls.Clear();
        if (!_open)
        {
            return [];
        }

        var sensors = new List<RawSensor>();
        foreach (var hardware in _computer.Hardware)
        {
            CollectHardware(hardware, sensors);
        }

        return sensors;
    }

    private void EnsureOpen()
    {
        if (_openAttempted)
        {
            return;
        }

        _openAttempted = true;
        try
        {
            _computer.Open();
            _open = true;
        }
        catch (Exception ex)
        {
            _openError = ex.Message;
            ExtensionLog.Write(ex.ToString());
        }
    }

    private void CollectHardware(IHardware hardware, List<RawSensor> sensors)
    {
        try
        {
            hardware.Update();
        }
        catch (Exception ex)
        {
            ExtensionLog.Write(ex.Message);
            return;
        }

        CollectSensors(hardware, sensors);
        foreach (var sub in hardware.SubHardware)
        {
            CollectHardware(sub, sensors);
        }
    }

    private void CollectSensors(IHardware hardware, List<RawSensor> sensors)
    {
        var kind = KindOf(hardware);
        foreach (var sensor in hardware.Sensors)
        {
            if (IsFanControl(sensor) && sensor.Control is not null)
            {
                _controls[sensor.Control.Identifier.ToString()] = sensor.Control;
            }

            if (kind is null || sensor.Value is null)
            {
                continue;
            }

            if (sensor.SensorType is not SensorType.Load and not SensorType.Temperature)
            {
                continue;
            }

            sensors.Add(new RawSensor(
                hardware.Identifier.ToString(),
                hardware.Name,
                kind.Value,
                ClassOf(hardware),
                sensor.Name,
                sensor.SensorType == SensorType.Load ? MetricKind.Load : MetricKind.Temperature,
                sensor.Value.Value));
        }
    }

    private static bool IsFanControl(ISensor sensor)
    {
        if (sensor.Control is null)
        {
            return false;
        }

        if (sensor.Name.Contains("pump", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return sensor.SensorType is SensorType.Fan or SensorType.Control
            || sensor.Name.Contains("fan", StringComparison.OrdinalIgnoreCase);
    }

    private static DeviceKind? KindOf(IHardware hardware) => hardware.HardwareType switch
    {
        HardwareType.Cpu => DeviceKind.Cpu,
        HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel => DeviceKind.Gpu,
        _ => null,
    };

    private static GpuClass ClassOf(IHardware hardware)
    {
        var arc = hardware.Name.Contains("Arc", StringComparison.OrdinalIgnoreCase);
        return hardware.HardwareType switch
        {
            HardwareType.GpuNvidia or HardwareType.GpuAmd => GpuClass.Discrete,
            HardwareType.GpuIntel => arc ? GpuClass.Discrete : GpuClass.Integrated,
            _ => GpuClass.None,
        };
    }

    private static float ScaleDuty(IControl control, float percent)
    {
        var min = control.MinSoftwareValue;
        var max = control.MaxSoftwareValue;
        if (max <= min)
        {
            return percent;
        }

        if (max <= 100f && min >= 0f)
        {
            return Math.Clamp(percent, min, max);
        }

        return min + ((max - min) * (Math.Clamp(percent, 0f, 100f) / 100f));
    }
}

internal static class ExtensionLog
{
    public static void Write(string message)
    {
        try
        {
            Microsoft.CommandPalette.Extensions.Toolkit.ExtensionHost.LogMessage(message);
        }
        catch
        {
            // The host logger is unavailable when the process is not activated.
        }
    }
}
