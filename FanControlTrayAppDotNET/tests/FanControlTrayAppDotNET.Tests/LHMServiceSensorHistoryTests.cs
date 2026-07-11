using FanControlTrayAppDotNET.Services;
using LibreHardwareMonitor.Hardware;
using Xunit;

namespace FanControlTrayAppDotNET.Tests;

public sealed class LHMServiceSensorHistoryTests
{
    [Fact]
    public void DisableUnusedSensorHistoryDisablesAndClearsHistory()
    {
        TestSensor sensor = new();

        LHMService.DisableUnusedSensorHistory(sensor);

        Assert.Equal(TimeSpan.Zero, sensor.ValuesTimeWindow);
        Assert.Empty(sensor.Values);
        Assert.Equal(1, sensor.ClearValuesCallCount);
    }

    private sealed class TestSensor : ISensor
    {
        private readonly List<SensorValue> _values =
        [
            new SensorValue(42.0f, DateTime.UtcNow)
        ];

        public int ClearValuesCallCount { get; private set; }
        public IControl Control => null!;
        public IHardware Hardware => null!;
        public Identifier Identifier { get; } = new("test", "sensor");
        public int Index => 0;
        public bool IsDefaultHidden => false;
        public float? Max => 42.0f;
        public float? Min => 42.0f;
        public string Name { get; set; } = "Test sensor";
        public IReadOnlyList<IParameter> Parameters { get; } = [];
        public SensorType SensorType => SensorType.Temperature;
        public float? Value => 42.0f;
        public IEnumerable<SensorValue> Values => _values;
        public TimeSpan ValuesTimeWindow { get; set; } = TimeSpan.FromDays(1.0);

        public void ResetMin()
        {
        }

        public void ResetMax()
        {
        }

        public void ClearValues()
        {
            ClearValuesCallCount++;
            _values.Clear();
        }

        public void Accept(IVisitor visitor)
        {
            visitor.VisitSensor(this);
        }

        public void Traverse(IVisitor visitor)
        {
        }
    }
}
