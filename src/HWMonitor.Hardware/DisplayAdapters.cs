// Copyright (c) 2026 Ondery
// SPDX-License-Identifier: MIT

using System.Runtime.InteropServices;
using HWMonitor.Core.Sensors;

namespace HWMonitor.Hardware;

public static class DisplayAdapters
{
    public static IReadOnlyList<AdapterDescriptor> TryEnumerate(Action<string>? log = null)
    {
        try
        {
            return Enumerate();
        }
        catch (Exception ex)
        {
            log?.Invoke(ex.Message);
            return [];
        }
    }

    public static IReadOnlyList<AdapterDescriptor> Enumerate()
    {
        var results = new List<AdapterDescriptor>();
        var iid = new Guid("770aae78-f26f-4dba-a829-253c83d1b387");
        if (CreateDXGIFactory1(ref iid, out var factory) < 0 || factory == 0)
        {
            return results;
        }

        try
        {
            var enumerate = GetDelegate<EnumAdapters1Delegate>(factory, 12);
            var release = GetDelegate<ReleaseDelegate>(factory, 2);
            for (uint index = 0; ; index++)
            {
                var hr = enumerate(factory, index, out var adapter);
                if (hr < 0 || adapter == 0)
                {
                    break;
                }

                try
                {
                    if (TryDescribe(adapter, out var descriptor))
                    {
                        results.Add(descriptor);
                    }
                }
                finally
                {
                    GetDelegate<ReleaseDelegate>(adapter, 2)(adapter);
                }
            }

            release(factory);
            factory = 0;
        }
        finally
        {
            if (factory != 0)
            {
                Marshal.Release(factory);
            }
        }

        return results;
    }

    private static bool TryDescribe(nint adapter, out AdapterDescriptor descriptor)
    {
        descriptor = new AdapterDescriptor(0, 0, string.Empty, GpuClass.None);
        var getDesc = GetDelegate<GetDesc1Delegate>(adapter, 10);
        var size = Marshal.SizeOf<DxgiAdapterDesc1>();
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (getDesc(adapter, buffer) < 0)
            {
                return false;
            }

            var desc = Marshal.PtrToStructure<DxgiAdapterDesc1>(buffer);
            if ((desc.Flags & 2) != 0 || desc.Description.Contains("Basic Render", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var hardwareType = desc.VendorId switch
            {
                DisplayVendors.Nvidia => "GpuNvidia",
                DisplayVendors.Amd => "GpuAmd",
                DisplayVendors.Intel => "GpuIntel",
                _ => string.Empty,
            };
            var runtimeType = desc.Description.Contains("Arc", StringComparison.OrdinalIgnoreCase) ? "Discrete" : string.Empty;
            var gpuClass = hardwareType.Length == 0
                ? GpuClass.None
                : GpuClassResolver.Resolve(new GpuClassSignals(hardwareType, runtimeType, string.Empty, desc.DedicatedVideoMemory));
            descriptor = new AdapterDescriptor(
                desc.LuidLow,
                unchecked((uint)desc.LuidHigh),
                desc.Description.Trim(),
                gpuClass,
                desc.DedicatedVideoMemory,
                desc.VendorId);
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static T GetDelegate<T>(nint instance, int slot)
        where T : Delegate
    {
        var vtable = Marshal.ReadIntPtr(instance);
        var method = Marshal.ReadIntPtr(vtable, slot * nint.Size);
        return Marshal.GetDelegateForFunctionPointer<T>(method);
    }

    [DllImport("dxgi.dll", ExactSpelling = true)]
    private static extern int CreateDXGIFactory1(ref Guid iid, out nint factory);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DxgiAdapterDesc1
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public ulong DedicatedVideoMemory;
        public ulong DedicatedSystemMemory;
        public ulong SharedSystemMemory;
        public uint LuidLow;
        public int LuidHigh;
        public uint Flags;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int EnumAdapters1Delegate(nint self, uint index, out nint adapter);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetDesc1Delegate(nint self, nint desc);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate uint ReleaseDelegate(nint self);
}
