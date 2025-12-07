using Agent.Orchestrator.Api.DTOs;
using Agent.Orchestrator.Api.Services;
using Microsoft.AspNetCore.SignalR;

namespace Agent.Orchestrator.Api.Hubs;

/// <summary>
/// SignalR Hub para streaming de eventos dos agentes em tempo real
/// </summary>
public class AgentHub : Hub
{
    private readonly ILogger<AgentHub> _logger;
    private readonly IAgentEventBus _eventBus;

    public AgentHub(ILogger<AgentHub> logger, IAgentEventBus eventBus)
    {
        _logger = logger;
        _eventBus = eventBus;
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("Client connected: {ConnectionId}", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Client disconnected: {ConnectionId}", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Cliente se inscreve para receber eventos de uma tarefa específica
    /// </summary>
    public async Task SubscribeToTask(string taskId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, taskId);
        _logger.LogInformation("Client {ConnectionId} subscribed to task {TaskId}", Context.ConnectionId, taskId);
    }

    /// <summary>
    /// Cliente cancela inscrição de uma tarefa
    /// </summary>
    public async Task UnsubscribeFromTask(string taskId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, taskId);
        _logger.LogInformation("Client {ConnectionId} unsubscribed from task {TaskId}", Context.ConnectionId, taskId);
    }

    /// <summary>
    /// Envia aprovação humana para uma ação
    /// </summary>
    public async Task ApproveAction(string taskId, string stepId, bool approved, string? comment = null)
    {
        _logger.LogInformation("Human approval for task {TaskId}, step {StepId}: {Approved}", taskId, stepId, approved);
        
        await Clients.Group(taskId).SendAsync("HumanApprovalReceived", new
        {
            TaskId = taskId,
            StepId = stepId,
            Approved = approved,
            Comment = comment,
            Timestamp = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Solicita cancelamento de uma tarefa
    /// </summary>
    public async Task CancelTask(string taskId)
    {
        _logger.LogInformation("Cancel requested for task {TaskId}", taskId);
        
        await Clients.Group(taskId).SendAsync("TaskCancellationRequested", new
        {
            TaskId = taskId,
            Timestamp = DateTime.UtcNow
        });
    }
}

/// <summary>
/// Serviço para enviar eventos do Hub
/// </summary>
public class AgentHubNotifier
{
    private readonly IHubContext<AgentHub> _hubContext;
    private readonly ILogger<AgentHubNotifier> _logger;

    public AgentHubNotifier(IHubContext<AgentHub> hubContext, ILogger<AgentHubNotifier> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task SendEventAsync(AgentEvent agentEvent)
    {
        try
        {
            await _hubContext.Clients.Group(agentEvent.TaskId).SendAsync(agentEvent.EventType, agentEvent);
            _logger.LogDebug("Sent event {EventType} to task {TaskId}", agentEvent.EventType, agentEvent.TaskId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending event {EventType} to task {TaskId}", agentEvent.EventType, agentEvent.TaskId);
        }
    }

    public async Task SendLogAsync(string taskId, string message, string level = "Info")
    {
        await SendEventAsync(new OnLogMessage
        {
            TaskId = taskId,
            Message = message,
            Level = level
        });
    }
}
