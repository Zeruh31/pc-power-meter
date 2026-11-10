using System.Runtime.InteropServices;
using Microsoft.Win32;
using PcPowerMeter.Core;

namespace PcPowerMeter.Sensors;

/// <summary>System-wide CPU times from GetSystemTimes (kernel time includes idle time).</summary>
internal sealed partial class SystemTimesCpuLoadSensor : ICpuLoadSensor
{
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetSystemTimes(out long idleTime, out long kernelTime, out long userTime);

    public CpuTimes? Read() =>
        GetSystemTimes(out long idle, out long kernel, out long user) ? new CpuTimes(idle, kernel + user) : null;

    /// <summary>Processor name from the registry (no WMI round trip), or null.</summary>
    public static string? ProcessorName()
    {
        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            return (key?.GetValue("ProcessorNameString") as string)?.Trim();
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }
}
