namespace Agent.Orchestrator.Api.DTOs;

/// <summary>
/// Plano de execução gerado pelo Planner
/// </summary>
public class ExecutionPlan
{
    public string PlanId { get; set; } = Guid.NewGuid().ToString();
    public string Objective { get; set; } = string.Empty;
    public List<TaskStep> Steps { get; set; } = new();
    public ExecutionMode Mode { get; set; } = ExecutionMode.Sequential;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int Version { get; set; } = 1;
    public bool RequiresHumanApproval { get; set; } = false;
    public string? HumanApprovalReason { get; set; }

    public TaskStep? GetNextPendingStep()
    {
        return Steps.FirstOrDefault(s => s.Status == StepStatus.Pending);
    }

    public bool IsComplete => Steps.All(s => s.Status == StepStatus.Completed || s.Status == StepStatus.Skipped);
    public bool HasFailed => Steps.Any(s => s.Status == StepStatus.Failed);
    public double Progress => Steps.Count > 0 ? (double)Steps.Count(s => s.Status == StepStatus.Completed) / Steps.Count : 0;
}

public class TaskStep
{
    public string StepId { get; set; } = Guid.NewGuid().ToString();
    public int Order { get; set; }
    public string AgentName { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public Dictionary<string, object> Parameters { get; set; } = new();
    public List<string> DependsOn { get; set; } = new();
    public StepStatus Status { get; set; } = StepStatus.Pending;
    public string? Result { get; set; }
    public string? Error { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public bool IsConditional { get; set; } = false;
    public string? Condition { get; set; }
    public int RetryCount { get; set; } = 0;
    public int MaxRetries { get; set; } = 3;
}

public enum StepStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Skipped,
    WaitingDependency,
    Retrying
}

public enum ExecutionMode
{
    Sequential,
    Parallel,
    Hierarchical,
    Conditional
}
