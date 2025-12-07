using System.Collections.Concurrent;

namespace Agent.Orchestrator.Api.DTOs;

/// <summary>
/// Contexto de execução de uma tarefa - Suporta agentes autônomos
/// </summary>
public class TaskExecutionContext
{
    public string TaskId { get; set; } = Guid.NewGuid().ToString();
    public string Prompt { get; set; } = string.Empty;
    public ConcurrentQueue<string> Logs { get; set; } = new();
    public bool IsCompleted { get; set; }
    public string? GeneratedCode { get; set; }
    public string? ReviewResult { get; set; }

    // Propriedades para agentes autônomos
    public ExecutionPlan? Plan { get; set; }
    public IntentResult? Intent { get; set; }
    public ConcurrentDictionary<string, object> SharedState { get; set; } = new();
    public List<AgentResult> AgentResults { get; set; } = new();
    public ExecutionStatus Status { get; set; } = ExecutionStatus.Pending;
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public int ReplanCount { get; set; } = 0;
    public List<string> Errors { get; set; } = new();
    public CancellationTokenSource CancellationTokenSource { get; set; } = new();

    public void AddResult(string agentName, object result)
    {
        AgentResults.Add(new AgentResult
        {
            AgentName = agentName,
            Result = result,
            Timestamp = DateTime.UtcNow
        });
        SharedState[$"{agentName}_LastResult"] = result;
    }

    public T? GetSharedState<T>(string key) where T : class
    {
        return SharedState.TryGetValue(key, out var value) ? value as T : null;
    }
}

public class AgentResult
{
    public string AgentName { get; set; } = string.Empty;
    public object Result { get; set; } = new();
    public DateTime Timestamp { get; set; }
    public bool Success { get; set; } = true;
    public string? Error { get; set; }
}

public enum ExecutionStatus
{
    Pending,
    Planning,
    Executing,
    Replanning,
    WaitingHumanApproval,
    Completed,
    Failed,
    Cancelled
}

public record TaskRequest(string Prompt, Dictionary<string, object>? Parameters = null);