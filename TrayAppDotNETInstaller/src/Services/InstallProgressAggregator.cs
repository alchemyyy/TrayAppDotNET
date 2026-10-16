namespace TrayAppDotNETInstaller.Services;

/// <summary>
/// Folds the progress of applications installing at the same time into one bar.
/// Each application reports its own 0-100 percent, and the bar shows their mean with the latest message.
/// </summary>
public sealed class InstallProgressAggregator
{
    // Stands in for System.Threading.Lock, which the .NET Framework does not define
    private readonly object _gate = new();
    private readonly int[] _applicationPercents;
    private readonly IProgress<InstallProgressLine> _progress;

    public InstallProgressAggregator(int applicationCount, IProgress<InstallProgressLine> progress)
    {
        FrameworkCompatibility.ThrowIfNull(progress, nameof(progress));
        if (applicationCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(applicationCount), applicationCount, message: "At least one application is required.");

        _applicationPercents = new int[applicationCount];
        _progress = progress;
    }

    /// <summary>The sink for the application at <paramref name="index"/>; it reports on the application's own 0-100 scale.</summary>
    public IProgress<InstallProgressLine> ForApplication(int index)
    {
        if (index < 0 || index >= _applicationPercents.Length)
            throw new ArgumentOutOfRangeException(nameof(index), index, message: "No such application.");

        return new ApplicationProgress(this, index);
    }

    private void Report(int index, InstallProgressLine line)
    {
        // The engine reports failures itself once every application has stopped, so a failure line here would
        // only move the bar to the end
        if (line.IsFailure) return;

        // Reporting under the gate keeps the forwarded lines in the order their means were computed
        lock (_gate)
        {
            // An application never moves backwards, so neither does the mean
            _applicationPercents[index] = Math.Max(_applicationPercents[index], line.Percent);
            int percentSum = 0;
            foreach (int applicationPercent in _applicationPercents) percentSum += applicationPercent;

            _progress.Report(InstallProgressLine.At(percentSum / _applicationPercents.Length, line.Message));
        }
    }

    private sealed class ApplicationProgress(InstallProgressAggregator aggregator, int index) : IProgress<InstallProgressLine>
    {
        public void Report(InstallProgressLine value)
        {
            FrameworkCompatibility.ThrowIfNull(value, nameof(value));

            aggregator.Report(index, value);
        }
    }
}
