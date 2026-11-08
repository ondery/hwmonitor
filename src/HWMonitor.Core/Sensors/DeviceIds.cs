// Copyright (c) 2026 Ondery
// SPDX-License-Identifier: MIT

namespace HWMonitor.Core.Sensors;

public static class DeviceIds
{
    public const string CoreMarker = ":core:";

    public static bool IsCore(string id) =>
        id.Contains(CoreMarker, StringComparison.Ordinal);

    public static string PackageId(string id)
    {
        var index = id.IndexOf(CoreMarker, StringComparison.Ordinal);
        return index < 0 ? id : id[..index];
    }
}
