// Copyright (c) 2026 Ondery
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using HWMonitor.Core.Power;

namespace HWMonitor;

internal sealed class PowerCfgGateway
{
    public bool TryRead(out CoolSnapshot? snapshot, out string? error)
    {
        snapshot = null;
        if (!TryRun("/getactivescheme", out var schemeText, out error))
        {
            return false;
        }

        if (!PowerCfgText.TryParseActiveScheme(schemeText, out var schemeId, out _))
        {
            error = "The active power scheme could not be identified.";
            return false;
        }

        var query = $"/query {schemeId} {PowerGuids.ProcessorSubgroup} {PowerGuids.MaxProcessorState}";
        if (!TryRun(query, out var throttleText, out error) || !PowerCfgText.TryParseIndexes(throttleText, out var ac, out var dc))
        {
            error ??= "Maximum processor state could not be read.";
            return false;
        }

        int? boostAc = null;
        int? boostDc = null;
        var boostQuery = $"/query {schemeId} {PowerGuids.ProcessorSubgroup} {PowerGuids.BoostMode}";
        if (TryRun(boostQuery, out var boostText, out _) && PowerCfgText.TryParseIndexes(boostText, out var parsedAc, out var parsedDc))
        {
            boostAc = parsedAc;
            boostDc = parsedDc;
        }

        snapshot = new CoolSnapshot(schemeId, ac, dc, boostAc, boostDc);
        error = null;
        return true;
    }

    public bool TryApply(CoolWrite write, out string? error, out string? warning)
    {
        warning = null;
        var subgroup = PowerGuids.ProcessorSubgroup;
        if (!TryRun($"/setacvalueindex {write.SchemeId} {subgroup} {PowerGuids.MaxProcessorState} {write.AcThrottleMax}", out _, out error))
        {
            return false;
        }

        if (!TryRun($"/setdcvalueindex {write.SchemeId} {subgroup} {PowerGuids.MaxProcessorState} {write.DcThrottleMax}", out _, out error))
        {
            return false;
        }

        if (write.AcBoost is int acBoost
            && !TryRun($"/setacvalueindex {write.SchemeId} {subgroup} {PowerGuids.BoostMode} {acBoost}", out _, out var boostError))
        {
            warning = boostError;
        }

        if (write.DcBoost is int dcBoost
            && !TryRun($"/setdcvalueindex {write.SchemeId} {subgroup} {PowerGuids.BoostMode} {dcBoost}", out _, out var dcError))
        {
            warning ??= dcError;
        }

        return TryRun($"/setactive {write.SchemeId}", out _, out error);
    }

    private static bool TryRun(string arguments, out string stdout, out string? error)
    {
        stdout = string.Empty;
        error = null;
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "powercfg.exe"),
                Arguments = arguments,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            process.Start();
            stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            if (!process.WaitForExit(8000))
            {
                process.Kill(entireProcessTree: true);
                error = "powercfg timed out.";
                return false;
            }

            if (process.ExitCode != 0)
            {
                error = string.IsNullOrWhiteSpace(stderr) ? stdout.Trim() : stderr.Trim();
                if (string.IsNullOrWhiteSpace(error))
                {
                    error = $"powercfg exited with code {process.ExitCode}.";
                }

                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
