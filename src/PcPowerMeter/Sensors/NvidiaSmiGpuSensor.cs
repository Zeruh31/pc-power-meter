using System.Diagnostics;
using System.Globalization;
using PcPowerMeter.Core;

namespace PcPowerMeter.Sensors;

/// <summary>
/// Fallback GPU sensor that runs nvidia-smi once per sample. Slower than NVML, but it
/// works when nvml.dll cannot be loaded directly.
/// </summary>
internal sealed class NvidiaSmiGpuSensor : IGpuSensor
{
    private const int TimeoutMs = 3000;
    private readonly string _exe;

    private NvidiaSmiGpuSensor(string exe, string? name)
    {
        _exe = exe;
        Name = name;
    }

    public PowerSource Source => PowerSource.NvidiaSmi;

    public string? Name { get; }

    public static NvidiaSmiGpuSensor? TryCreate()
    {
        string? exe = FindExe();
        if (exe is null)
        {
            return null;
        }
        var probe = new NvidiaSmiGpuSensor(exe, null);
        if (probe.Read() is null)
        {
            return null;
        }
        string[]? names = Run(exe, "--query-gpu=name --format=csv,noheader");
        string? name = names is { Length: > 0 } ? string.Join(" + ", names.Select(n => n.Trim())) : null;
        return new NvidiaSmiGpuSensor(exe, name);
    }

    public GpuReading? Read()
    {
        string[]? lines = Run(_exe, "--query-gpu=power.draw,utilization.gpu --format=csv,noheader,nounits");
        if (lines is null || lines.Length == 0)
        {
            return null;
        }
        double watts = 0, util = 0;
        int utilCount = 0;
        foreach (string line in lines)
        {
            string[] parts = line.Split(',');
            if (!double.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double p))
            {
                return null; // "[N/A]" or similar: a partial sum would understate the total
            }
            watts += p;
            if (parts.Length > 1 &&
                double.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double u))
            {
                util += u;
                utilCount++;
            }
        }
        return new GpuReading(watts, utilCount > 0 ? util / utilCount : null);
    }

    public void Dispose()
    {
    }

    /// <summary>
    /// Only the two places the NVIDIA driver installs it. PATH is deliberately not searched,
    /// so a stray nvidia-smi.exe elsewhere is never started.
    /// </summary>
    private static string? FindExe()
    {
        string system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string[] candidates =
        {
            Path.Combine(system, "nvidia-smi.exe"),
            Path.Combine(programFiles, "NVIDIA Corporation", "NVSMI", "nvidia-smi.exe"),
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static string[]? Run(string exe, string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo(exe, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using Process? process = Process.Start(psi);
            if (process is null)
            {
                return null;
            }
            Task<string> output = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(TimeoutMs))
            {
                try
                {
                    process.Kill();
                }
                catch (InvalidOperationException)
                {
                }
                return null;
            }
            if (process.ExitCode != 0)
            {
                return null;
            }
            return output.Result.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return null;
        }
    }
}
