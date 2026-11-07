// Copyright (c) 2026 Ondery
// SPDX-License-Identifier: MIT

using System.Text.Json;
using HWMonitor.Core.Options;
using HWMonitor.Core.Power;
using HWMonitor.Core.Text;

namespace HWMonitor;

internal sealed class CoolModeService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly PowerCfgGateway _gateway = new();
    private readonly string _path;
    private CoolState _state = CoolState.Idle;

    public CoolModeService(string directory)
    {
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "cool-mode.json");
        _state = ReadFile();
    }

    public bool IsOn => _state.Applied;

    public bool ActivatedByAuto => _state.ActivatedByAuto;

    public string? LastError { get; private set; }

    public void RecoverOnStartup()
    {
        if (!_state.Applied)
        {
            return;
        }

        Disable();
    }

    public void Enable(CoolOptions options, bool activatedByAuto)
    {
        if (!_gateway.TryRead(out var snapshot, out var readError) || snapshot is null)
        {
            LastError = string.IsNullOrWhiteSpace(readError) ? UiText.CoolReadFailed : UiText.CoolReadFailed;
            return;
        }

        var (state, write) = CoolModePlanner.Enable(_state, snapshot, options.MaximumProcessorState, options.LimitBoost, options.BoostValue, activatedByAuto);
        if (write is null || !Apply(state, write))
        {
            return;
        }

        LastError = null;
    }

    public void Disable()
    {
        var (state, write) = CoolModePlanner.Disable(_state);
        if (write is null)
        {
            _state = CoolState.Idle;
            WriteFile(_state);
            LastError = null;
            return;
        }

        if (Apply(state, write))
        {
            LastError = null;
        }
    }

    private bool Apply(CoolState state, CoolWrite write)
    {
        if (!_gateway.TryApply(write, out var error, out _))
        {
            LastError = UiText.CoolFailed;
            ExtensionLog.Write(error ?? LastError);
            return false;
        }

        _state = state;
        WriteFile(state);
        return true;
    }

    private CoolState ReadFile()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return CoolState.Idle;
            }

            var stored = JsonSerializer.Deserialize<StoredCool>(File.ReadAllText(_path));
            if (stored is null)
            {
                return CoolState.Idle;
            }

            return new CoolState(
                stored.Applied,
                stored.SchemeId,
                stored.AcThrottleMax,
                stored.DcThrottleMax,
                stored.AcBoost,
                stored.DcBoost,
                stored.BoostCaptured,
                stored.ActivatedByAuto);
        }
        catch (Exception ex)
        {
            ExtensionLog.Write(ex.Message);
            return CoolState.Idle;
        }
    }

    private void WriteFile(CoolState state)
    {
        var stored = new StoredCool
        {
            Applied = state.Applied,
            SchemeId = state.SchemeId,
            AcThrottleMax = state.AcThrottleMax,
            DcThrottleMax = state.DcThrottleMax,
            AcBoost = state.AcBoost,
            DcBoost = state.DcBoost,
            BoostCaptured = state.BoostCaptured,
            ActivatedByAuto = state.ActivatedByAuto,
        };
        File.WriteAllText(_path, JsonSerializer.Serialize(stored, JsonOptions));
    }

    private sealed class StoredCool
    {
        public bool Applied { get; set; }

        public string? SchemeId { get; set; }

        public int? AcThrottleMax { get; set; }

        public int? DcThrottleMax { get; set; }

        public int? AcBoost { get; set; }

        public int? DcBoost { get; set; }

        public bool BoostCaptured { get; set; }

        public bool ActivatedByAuto { get; set; }
    }
}
