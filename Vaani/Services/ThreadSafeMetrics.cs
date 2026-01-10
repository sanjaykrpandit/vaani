namespace Vaani.Services;

/// <summary>
/// Thread-safe utility for updating metrics and peaks.
/// </summary>
public static class ThreadSafeMetrics
{
    /// <summary>
    /// Updates a peak value in a thread-safe manner using lock-free operations.
    /// </summary>
    public static void UpdatePeak(ref int peak, int current)
    {
        int initialValue, computedValue;
        do
        {
            initialValue = peak;
            computedValue = Math.Max(initialValue, current);
        }
        while (Interlocked.CompareExchange(ref peak, computedValue, initialValue) != initialValue);
    }
}