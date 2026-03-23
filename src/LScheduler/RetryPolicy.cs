namespace LScheduler;

public sealed class RetryPolicy
{
    public int MaxRetries { get; set; } = 3;

    public TimeSpan RetryInterval { get; set; } = TimeSpan.FromSeconds(10);

    public bool UseExponentialBackoff { get; set; }

    internal RetryPolicy Clone()
    {
        return new RetryPolicy
        {
            MaxRetries = MaxRetries,
            RetryInterval = RetryInterval,
            UseExponentialBackoff = UseExponentialBackoff,
        };
    }
}
