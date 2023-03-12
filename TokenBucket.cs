namespace ScOtaServer;

internal class TokenBucket
{
    private readonly object _lock = new object();
    private readonly int _capacity;
    private readonly int _refillRate;
    private int _tokens;
    private DateTimeOffset _lastRefillTime;

    public TokenBucket(int capacity, int refillRate)
    {
        _capacity = capacity;
        _refillRate = refillRate;
        _tokens = capacity;
        _lastRefillTime = DateTimeOffset.UtcNow;
    }

    public bool TryConsumeToken(int count)
    {
        lock (_lock)
        {
            Refill();

            if (_tokens < count)
            {
                return false;
            }

            _tokens -= count;
            return true;
        }
    }

    private void Refill()
    {
        var now = DateTimeOffset.UtcNow;
        var timeSinceLastRefill = now - _lastRefillTime;
        var newTokens = (int)(timeSinceLastRefill.TotalMilliseconds / _refillRate);

        if (newTokens > 0)
        {
            _tokens = Math.Min(_tokens + newTokens, _capacity);
            _lastRefillTime = now;
        }
    }
}
