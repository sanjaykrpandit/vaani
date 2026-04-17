using BenchmarkDotNet.Attributes;
using Vaani.Services;
using Microsoft.VSDiagnostics;

namespace Vaani.Benchmarks;
[CPUUsageDiagnoser]
public class DeviceServiceBenchmarks
{
    private DeviceService _deviceService = null!;
    [GlobalSetup]
    public void Setup()
    {
        _deviceService = DeviceService.Instance;
        _deviceService.RefreshDeviceCache();
    }

    [Benchmark]
    public int GetAllDevices_Count()
    {
        var(inputs, outputs) = _deviceService.GetAllDevices();
        return inputs.Count + outputs.Count;
    }
}