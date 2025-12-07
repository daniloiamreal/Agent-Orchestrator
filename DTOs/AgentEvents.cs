namespace Agent.Orchestrator.Api.DTOs;

/// <summary>
/// Eventos emitidos pelos agentes para o SignalR Hub
/// </summary>
public abstract class AgentEvent
{
    public string EventId { get; set; } = Guid.NewGuid().ToString();
    public string TaskId { get; set; } = string.Empty;
    public string AgentName { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public abstract string EventType { get; }
}

public class OnPlanCreated : AgentEvent
{
    public override string EventType => nameof(OnPlanCreated);
    public ExecutionPlan Plan { get; set; } = new();
}

public class OnPlanUpdated : AgentEvent
{
    public override string EventType => nameof(OnPlanUpdated);
    public ExecutionPlan Plan { get; set; } = new();
    public string Reason { get; set; } = string.Empty;
}

public class OnAgentStart : AgentEvent
{
    public override string EventType => nameof(OnAgentStart);
    public string Action { get; set; } = string.Empty;
    public Dictionary<string, object> Parameters { get; set; } = new();
}

public class OnAgentAction : AgentEvent
{
    public override string EventType => nameof(OnAgentAction);
    public string Action { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public class OnAgentResult : AgentEvent
{
    public override string EventType => nameof(OnAgentResult);
    public object Result { get; set; } = new();
    public bool Success { get; set; } = true;
    public TimeSpan Duration { get; set; }
}

public class OnAgentError : AgentEvent
{
    public override string EventType => nameof(OnAgentError);
    public string Error { get; set; } = string.Empty;
    public string? StackTrace { get; set; }
    public bool WillRetry { get; set; }
    public int RetryAttempt { get; set; }
}

public class OnToolCall : AgentEvent
{
    public override string EventType => nameof(OnToolCall);
    public string ToolName { get; set; } = string.Empty;
    public Dictionary<string, object> Input { get; set; } = new();
    public object? Output { get; set; }
    public bool Success { get; set; }
}

public class OnReplan : AgentEvent
{
    public override string EventType => nameof(OnReplan);
    public string Reason { get; set; } = string.Empty;
    public ExecutionPlan OldPlan { get; set; } = new();
    public ExecutionPlan NewPlan { get; set; } = new();
}

public class OnWorkflowCompleted : AgentEvent
{
    public override string EventType => nameof(OnWorkflowCompleted);
    public bool Success { get; set; }
    public TimeSpan TotalDuration { get; set; }
    public List<AgentResult> Results { get; set; } = new();
    public string? Summary { get; set; }
}

public class OnStatusChanged : AgentEvent
{
    public override string EventType => nameof(OnStatusChanged);
    public ExecutionStatus OldStatus { get; set; }
    public ExecutionStatus NewStatus { get; set; }
}

public class OnHumanApprovalRequired : AgentEvent
{
    public override string EventType => nameof(OnHumanApprovalRequired);
    public string Reason { get; set; } = string.Empty;
    public string ActionDescription { get; set; } = string.Empty;
    public Dictionary<string, object> Context { get; set; } = new();
}

public class OnLogMessage : AgentEvent
{
    public override string EventType => nameof(OnLogMessage);
    public string Message { get; set; } = string.Empty;
    public string Level { get; set; } = "Info";
}
