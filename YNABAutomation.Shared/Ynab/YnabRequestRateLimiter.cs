using System.Collections.Concurrent;

namespace YNABAutomationConsole.Ynab;

public interface IYnabRequestRateLimiter
{
    Task WaitAsync(CancellationToken cancellationToken);

    Task PauseAsync(TimeSpan duration, CancellationToken cancellationToken);
}

public sealed class YnabRequestRateLimiter : IYnabRequestRateLimiter
{
    private static readonly TimeSpan Window = TimeSpan.FromHours(1);
    private const int MaximumRequests = 200;

    private readonly ConcurrentQueue<DateTimeOffset> _requests = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset _pausedUntil;

    public async Task WaitAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            TimeSpan delay;
            await _gate.WaitAsync(cancellationToken);
            try
            {
                var now = DateTimeOffset.UtcNow;
                RemoveExpiredRequests(now);
                delay = _pausedUntil > now
                    ? _pausedUntil - now
                    : _requests.Count < MaximumRequests
                        ? TimeSpan.Zero
                        : _requests.TryPeek(out var oldest)
                            ? oldest + Window - now
                            : TimeSpan.Zero;

                if (delay <= TimeSpan.Zero)
                {
                    _requests.Enqueue(now);
                    return;
                }
            }
            finally
            {
                _gate.Release();
            }

            await Task.Delay(delay, cancellationToken);
        }
    }

    public async Task PauseAsync(TimeSpan duration, CancellationToken cancellationToken)
    {
        if (duration <= TimeSpan.Zero)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var pausedUntil = DateTimeOffset.UtcNow + duration;
            if (pausedUntil > _pausedUntil)
            {
                _pausedUntil = pausedUntil;
            }
        }
        finally
        {
            _gate.Release();
        }

        await Task.Delay(duration, cancellationToken);
    }

    private void RemoveExpiredRequests(DateTimeOffset now)
    {
        while (_requests.TryPeek(out var timestamp)
            && timestamp + Window <= now)
        {
            _requests.TryDequeue(out _);
        }
    }
}
