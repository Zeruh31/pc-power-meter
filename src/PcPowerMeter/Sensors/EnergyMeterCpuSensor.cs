using System.Management;
using System.Runtime.InteropServices;
using PcPowerMeter.Core;

namespace PcPowerMeter.Sensors;

/// <summary>
/// CPU package energy from the Windows Energy Meter performance counters, which expose
/// the processor's RAPL package energy counter (Intel and many AMD CPUs). Read through
/// WMI; no driver and no administrator rights are needed. "Energy" is in picowatt-hours
/// and "Time" is the meter's own timestamp in milliseconds.
/// </summary>
internal sealed class EnergyMeterCpuSensor : ICpuEnergySensor
{
    private const string Query =
        "SELECT Name, Energy, Time FROM Win32_PerfRawData_PowerMeterCounter_EnergyMeter " +
        "WHERE Name LIKE 'RAPL_Package%_PKG'";

    private readonly ManagementObjectSearcher _searcher = new(@"root\cimv2", Query);

    public EnergySnapshot? Read()
    {
        try
        {
            double energy = 0, time = 0;
            int packages = 0;
            using ManagementObjectCollection rows = _searcher.Get();
            foreach (ManagementBaseObject row in rows)
            {
                using (row)
                {
                    energy += Convert.ToDouble(row["Energy"]);
                    if (packages == 0)
                    {
                        time = Convert.ToDouble(row["Time"]);
                    }
                    packages++;
                }
            }
            return packages > 0 ? new EnergySnapshot(energy, time) : null;
        }
        catch (Exception ex) when (ex is ManagementException or COMException or InvalidCastException
                                       or FormatException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Dispose() => _searcher.Dispose();
}
