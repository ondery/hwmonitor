// Copyright (c) 2026 Ondery
// SPDX-License-Identifier: MIT

using HWMonitor.Core.Sensors;
using LibreHardwareMonitor.Hardware;

namespace HWMonitor.Hardware;

/// <summary>
/// Reads CPU and GPU load and temperature sensors. It does not write
/// embedded-controller ports or vendor control methods.
/// </summary>
public sealed class HardwareProbe : IDisposable
{
    private readonly Computer _computer = new()
    {
        IsCpuEnabled = true,
        IsGpuEnabled = true,
        IsMotherboardEnabled = false,
        IsControllerEnabled = false,
        IsMemoryEnabled = false,
        IsNetworkEnabled = false,
        IsStorageEnabled = false,
        IsBatteryEnabled = false,
        IsPsuEnabled = false,
    };

    private readonly object _gate = new();
    private readonly Action<string>? _log;
    private bool _openAttempted;
    private bool _open;

    public HardwareProbe(Action<string>? log = null)
    {
        _log = log;
    }

    public string? OpenError { get; private set; }

    public IReadOnlyList<RawSensor> Read()
    {
        lock (_gate)
        {
            EnsureOpen();
            if (!_open)
            {
                return [];
            }

            var adapters = DisplayAdapters.TryEnumerate(_log);
            var sensors = new List<RawSensor>();
            var used = new HashSet<int>();
            foreach (var hardware in _computer.Hardware)
            {
                CollectHardware(hardware, sensors, adapters, used);
            }

            return sensors;
        }
    }

    public void Release()
    {
        lock (_gate)
        {
            CloseComputer();
            _openAttempted = false;
            OpenError = null;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            CloseComputer();
        }
    }

    private void CloseComputer()
    {
        if (!_open)
        {
            return;
        }

        try
        {
            _computer.Close();
        }
        catch (Exception ex)
        {
            _log?.Invoke(ex.Message);
        }

        _open = false;
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
            OpenError = ex.Message;
            _log?.Invoke(ex.Message);
        }
    }

    private void CollectHardware(
        IHardware hardware,
        List<RawSensor> sensors,
        IReadOnlyList<AdapterDescriptor> adapters,
        ISet<int> used)
    {
        try
        {
            hardware.Update();
        }
        catch (Exception ex)
        {
            _log?.Invoke(ex.Message);
            return;
        }

        CollectSensors(hardware, sensors, adapters, used);
        foreach (var sub in hardware.SubHardware)
        {
            CollectHardware(sub, sensors, adapters, used);
        }
    }

    private static void CollectSensors(
        IHardware hardware,
        List<RawSensor> sensors,
        IReadOnlyList<AdapterDescriptor> adapters,
        ISet<int> used)
    {
        var kind = KindOf(hardware);
        if (kind is null)
        {
            return;
        }

        var gpuClass = GpuClass.None;
        if (kind == DeviceKind.Gpu)
        {
            var match = LibraryAdapterMatch.Match(hardware.HardwareType.ToString(), hardware.Name, adapters, used);
            gpuClass = GpuClassResolver.Resolve(new GpuClassSignals(
                hardware.HardwareType.ToString(),
                hardware.GetType().Name,
                hardware.Identifier.ToString(),
                match?.DedicatedVideoBytes));
        }

        foreach (var sensor in hardware.Sensors)
        {
            if (sensor.Value is null || sensor.SensorType is not SensorType.Load and not SensorType.Temperature)
            {
                continue;
            }

            sensors.Add(new RawSensor(
                hardware.Identifier.ToString(),
                hardware.Name,
                kind.Value,
                gpuClass,
                sensor.Name,
                sensor.SensorType == SensorType.Load ? MetricKind.Load : MetricKind.Temperature,
                sensor.Value.Value));
        }
    }

    private static DeviceKind? KindOf(IHardware hardware) => hardware.HardwareType switch
    {
        HardwareType.Cpu => DeviceKind.Cpu,
        HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel => DeviceKind.Gpu,
        _ => null,
    };
}
