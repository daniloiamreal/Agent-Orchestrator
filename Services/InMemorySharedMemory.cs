using System.Collections.Concurrent;
using System.Text.Json;

namespace Agent.Orchestrator.Api.Services;

/// <summary>
/// Implementação em memória da memória compartilhada
/// </summary>
public class InMemorySharedMemory : ISharedMemory
{
    private readonly ConcurrentDictionary<string, (object Value, DateTime? Expiry)> _store = new();
    private readonly ConcurrentDictionary<string, List<string>> _history = new();
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, object>> _results = new();
    private readonly ILogger<InMemorySharedMemory> _logger;

    public InMemorySharedMemory(ILogger<InMemorySharedMemory> logger)
    {
        _logger = logger;
    }

    public Task SetAsync<T>(string key, T value, TimeSpan? expiry = null)
    {
        var expiryTime = expiry.HasValue ? DateTime.UtcNow.Add(expiry.Value) : (DateTime?)null;
        _store[key] = (value!, expiryTime);
        _logger.LogDebug("Set key: {Key}", key);
        return Task.CompletedTask;
    }

    public Task<T?> GetAsync<T>(string key) where T : class
    {
        if (_store.TryGetValue(key, out var entry))
        {
            if (entry.Expiry.HasValue && entry.Expiry.Value < DateTime.UtcNow)
            {
                _store.TryRemove(key, out _);
                return Task.FromResult<T?>(null);
            }

            if (entry.Value is T typedValue)
            {
                return Task.FromResult<T?>(typedValue);
            }

            if (entry.Value is JsonElement jsonElement)
            {
                var deserialized = JsonSerializer.Deserialize<T>(jsonElement.GetRawText());
                return Task.FromResult(deserialized);
            }
        }
        return Task.FromResult<T?>(null);
    }

    public Task<bool> ExistsAsync(string key)
    {
        if (_store.TryGetValue(key, out var entry))
        {
            if (entry.Expiry.HasValue && entry.Expiry.Value < DateTime.UtcNow)
            {
                _store.TryRemove(key, out _);
                return Task.FromResult(false);
            }
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }

    public Task RemoveAsync(string key)
    {
        _store.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    public Task<IEnumerable<string>> GetKeysAsync(string pattern)
    {
        var keys = _store.Keys.Where(k => k.Contains(pattern, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(keys);
    }

    public Task<Dictionary<string, object>> GetAllAsync()
    {
        var result = _store
            .Where(kv => !kv.Value.Expiry.HasValue || kv.Value.Expiry.Value >= DateTime.UtcNow)
            .ToDictionary(kv => kv.Key, kv => kv.Value.Value);
        return Task.FromResult(result);
    }

    public Task ClearAsync()
    {
        _store.Clear();
        _history.Clear();
        _results.Clear();
        return Task.CompletedTask;
    }

    public Task AppendToHistoryAsync(string taskId, string entry)
    {
        var history = _history.GetOrAdd(taskId, _ => new List<string>());
        lock (history)
        {
            history.Add($"[{DateTime.UtcNow:HH:mm:ss.fff}] {entry}");
        }
        return Task.CompletedTask;
    }

    public Task<List<string>> GetHistoryAsync(string taskId, int limit = 100)
    {
        if (_history.TryGetValue(taskId, out var history))
        {
            lock (history)
            {
                return Task.FromResult(history.TakeLast(limit).ToList());
            }
        }
        return Task.FromResult(new List<string>());
    }

    public Task SaveResultAsync(string taskId, string agentName, object result)
    {
        var taskResults = _results.GetOrAdd(taskId, _ => new ConcurrentDictionary<string, object>());
        taskResults[agentName] = result;
        return Task.CompletedTask;
    }

    public Task<object?> GetResultAsync(string taskId, string agentName)
    {
        if (_results.TryGetValue(taskId, out var taskResults))
        {
            return Task.FromResult(taskResults.TryGetValue(agentName, out var result) ? result : null);
        }
        return Task.FromResult<object?>(null);
    }
}
