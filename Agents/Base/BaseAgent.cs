using Agent.Orchestrator.Api.DTOs;
using Agent.Orchestrator.Api.Services;

namespace Agent.Orchestrator.Api.Agents.Base;

/// <summary>
/// Classe base abstrata com funcionalidades comuns a todos os agentes
/// </summary>
public abstract class BaseAgent : IAgent
{
    protected readonly ILLMService _llmService;
    protected readonly IWorkspaceService _workspaceService;
    protected readonly IAgentEventBus _eventBus;
    protected readonly ISharedMemory _memory;
    protected readonly ILogger _logger;

    public abstract string Name { get; }
    public abstract string Description { get; }
    public abstract IReadOnlyList<string> Capabilities { get; }

    protected BaseAgent(
        ILLMService llmService,
        IWorkspaceService workspaceService,
        IAgentEventBus eventBus,
        ISharedMemory memory,
        ILogger logger)
    {
        _llmService = llmService;
        _workspaceService = workspaceService;
        _eventBus = eventBus;
        _memory = memory;
        _logger = logger;
    }

    public abstract Task<AgentResult> ExecuteAsync(
        TaskStep step, 
        TaskExecutionContext context, 
        CancellationToken cancellationToken = default);

    public virtual bool CanHandle(string action)
    {
        return Capabilities.Any(c => c.Equals(action, StringComparison.OrdinalIgnoreCase));
    }

    public virtual Task<bool> ValidateAsync(TaskStep step, TaskExecutionContext context)
    {
        return Task.FromResult(true);
    }

    protected async Task EmitEventAsync(AgentEvent agentEvent)
    {
        agentEvent.AgentName = Name;
        await _eventBus.PublishAsync(agentEvent);
    }

    protected void LogStep(TaskExecutionContext context, string message)
    {
        var formattedMessage = $"[{Name}] {message}";
        context.Logs.Enqueue(formattedMessage);
        _logger.LogInformation(formattedMessage);
    }

    protected async Task<string> CallLLMWithRetryAsync(
        string prompt, 
        int maxRetries = 3, 
        CancellationToken cancellationToken = default)
    {
        var attempt = 0;
        Exception? lastException = null;

        while (attempt < maxRetries)
        {
            try
            {
                return await _llmService.GenerateResponseAsync(prompt, cancellationToken);
            }
            catch (Exception ex)
            {
                lastException = ex;
                attempt++;
                _logger.LogWarning(ex, "LLM call failed, attempt {Attempt}/{MaxRetries}", attempt, maxRetries);
                
                if (attempt < maxRetries)
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), cancellationToken);
                }
            }
        }

        throw new Exception($"Failed after {maxRetries} attempts", lastException);
    }

    protected async Task SaveToMemoryAsync(string key, object value)
    {
        await _memory.SetAsync(key, value);
    }

    protected async Task<T?> GetFromMemoryAsync<T>(string key) where T : class
    {
        return await _memory.GetAsync<T>(key);
    }
}
