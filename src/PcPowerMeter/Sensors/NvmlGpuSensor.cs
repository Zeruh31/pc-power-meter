using System.Runtime.InteropServices;
using System.Text;
using PcPowerMeter.Core;

namespace PcPowerMeter.Sensors;

/// <summary>
/// GPU board power through the NVIDIA Management Library (nvml.dll, shipped with the
/// NVIDIA driver). The library is loaded at run time, so the app also starts on
/// machines without an NVIDIA GPU.
/// </summary>
internal sealed unsafe class NvmlGpuSensor : IGpuSensor
{
    private const int NvmlSuccess = 0;
    private const int NameBufferSize = 96; // NVML_DEVICE_NAME_V2_BUFFER_SIZE

    private readonly nint _library;
    private readonly nint[] _devices;
    private readonly delegate* unmanaged<int> _shutdown;
    private readonly delegate* unmanaged<nint, uint*, int> _getPowerUsage;
    private readonly delegate* unmanaged<nint, NvmlUtilization*, int> _getUtilization;
    private bool _disposed;

    [StructLayout(LayoutKind.Sequential)]
    private struct NvmlUtilization
    {
        public uint Gpu;
        public uint Memory;
    }

    private NvmlGpuSensor(nint library, nint[] devices, string? name,
        delegate* unmanaged<int> shutdown,
        delegate* unmanaged<nint, uint*, int> getPowerUsage,
        delegate* unmanaged<nint, NvmlUtilization*, int> getUtilization)
    {
        _library = library;
        _devices = devices;
        Name = name;
        _shutdown = shutdown;
        _getPowerUsage = getPowerUsage;
        _getUtilization = getUtilization;
    }

    public PowerSource Source => PowerSource.Nvml;

    public string? Name { get; }

    /// <summary>Returns a sensor, or null when NVML is missing or no GPU reports power.</summary>
    public static NvmlGpuSensor? TryCreate()
    {
        nint lib = LoadLibrary();
        if (lib == 0)
        {
            return null;
        }
        try
        {
            var init = (delegate* unmanaged<int>)NativeLibrary.GetExport(lib, "nvmlInit_v2");
            var shutdown = (delegate* unmanaged<int>)NativeLibrary.GetExport(lib, "nvmlShutdown");
            var getCount = (delegate* unmanaged<uint*, int>)NativeLibrary.GetExport(lib, "nvmlDeviceGetCount_v2");
            var getHandle = (delegate* unmanaged<uint, nint*, int>)NativeLibrary.GetExport(lib, "nvmlDeviceGetHandleByIndex_v2");
            var getName = (delegate* unmanaged<nint, byte*, uint, int>)NativeLibrary.GetExport(lib, "nvmlDeviceGetName");
            var getPower = (delegate* unmanaged<nint, uint*, int>)NativeLibrary.GetExport(lib, "nvmlDeviceGetPowerUsage");
            var getUtil = (delegate* unmanaged<nint, NvmlUtilization*, int>)NativeLibrary.GetExport(lib, "nvmlDeviceGetUtilizationRates");

            if (init() != NvmlSuccess)
            {
                NativeLibrary.Free(lib);
                return null;
            }

            uint count = 0;
            if (getCount(&count) != NvmlSuccess || count == 0)
            {
                shutdown();
                NativeLibrary.Free(lib);
                return null;
            }

            var devices = new List<nint>();
            var names = new List<string>();
            byte* buffer = stackalloc byte[NameBufferSize];
            for (uint i = 0; i < count; i++)
            {
                nint device;
                if (getHandle(i, &device) != NvmlSuccess)
                {
                    continue; // e.g. NVML_ERROR_NO_PERMISSION for one device
                }
                uint mw;
                if (getPower(device, &mw) != NvmlSuccess)
                {
                    continue; // this device does not report power
                }
                devices.Add(device);
                if (getName(device, buffer, NameBufferSize) == NvmlSuccess)
                {
                    int len = 0;
                    while (len < NameBufferSize && buffer[len] != 0)
                    {
                        len++;
                    }
                    names.Add(Encoding.UTF8.GetString(buffer, len).Trim());
                }
            }

            if (devices.Count == 0)
            {
                shutdown();
                NativeLibrary.Free(lib);
                return null;
            }
            string? name = names.Count > 0 ? string.Join(" + ", names) : null;
            return new NvmlGpuSensor(lib, devices.ToArray(), name, shutdown, getPower, getUtil);
        }
        catch (EntryPointNotFoundException)
        {
            NativeLibrary.Free(lib);
            return null;
        }
    }

    public GpuReading? Read()
    {
        if (_disposed)
        {
            return null;
        }
        double watts = 0, util = 0;
        int powerCount = 0, utilCount = 0;
        foreach (nint device in _devices)
        {
            uint mw;
            if (_getPowerUsage(device, &mw) != NvmlSuccess)
            {
                continue;
            }
            watts += mw / 1000.0;
            powerCount++;
            NvmlUtilization u;
            if (_getUtilization(device, &u) == NvmlSuccess)
            {
                util += u.Gpu;
                utilCount++;
            }
        }
        // A partial sum would understate the total, so any missing device means "unavailable".
        if (powerCount != _devices.Length)
        {
            return null;
        }
        return new GpuReading(watts, utilCount > 0 ? util / utilCount : null);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _shutdown();
        NativeLibrary.Free(_library);
    }

    private static nint LoadLibrary()
    {
        string system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string[] candidates =
        {
            Path.Combine(system, "nvml.dll"),
            Path.Combine(programFiles, "NVIDIA Corporation", "NVSMI", "nvml.dll"),
        };
        foreach (string path in candidates)
        {
            if (File.Exists(path) && NativeLibrary.TryLoad(path, out nint handle))
            {
                return handle;
            }
        }
        return 0;
    }
}
