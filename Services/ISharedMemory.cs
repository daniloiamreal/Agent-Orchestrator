namespace Agent.Orchestrator.Api.Services;

/// <summary>
/// Interface para memória compartilhada entre agentes
/// </summary>
public interface ISharedMemory
{
    Task SetAsync<T>(string key, T value, TimeSpan? expiry = null);
    Task<T?> GetAsync<T>(string key) where T : class;
    Task<bool> ExistsAsync(string key);
    Task RemoveAsync(string key);
    Task<IEnumerable<string>> GetKeysAsync(string pattern);
    Task<Dictionary<string, object>> GetAllAsync();
    Task ClearAsync();
    
    // Histórico
    Task AppendToHistoryAsync(string taskId, string entry);
    Task<List<string>> GetHistoryAsync(string taskId, int limit = 100);
    
    // Resultados intermediários
    Task SaveResultAsync(string taskId, string agentName, object result);
    Task<object?> GetResultAsync(string taskId, string agentName);
}
