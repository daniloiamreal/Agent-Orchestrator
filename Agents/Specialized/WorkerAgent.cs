using Agent.Orchestrator.Api.Agents.Base;
using Agent.Orchestrator.Api.DTOs;
using Agent.Orchestrator.Api.Services;

namespace Agent.Orchestrator.Api.Agents.Specialized;

/// <summary>
/// Agente Worker genérico para tarefas específicas
/// </summary>
public class WorkerAgent : BaseAgent
{
    private readonly string _workerName;
    private readonly string _workerDescription;
    private readonly List<string> _workerCapabilities;

    public override string Name => _workerName;
    public override string Description => _workerDescription;
    public override IReadOnlyList<string> Capabilities => _workerCapabilities;

    public WorkerAgent(
        string workerName,
        string workerDescription,
        IEnumerable<string> capabilities,
        ILLMService llmService,
        IWorkspaceService workspaceService,
        IAgentEventBus eventBus,
        ISharedMemory memory,
        ILogger<WorkerAgent> logger)
        : base(llmService, workspaceService, eventBus, memory, logger)
    {
        _workerName = workerName;
        _workerDescription = workerDescription;
        _workerCapabilities = capabilities.ToList();
    }

    public override async Task<AgentResult> ExecuteAsync(
        TaskStep step,
        TaskExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;
        LogStep(context, $"?? {Name} iniciado - Ação: {step.Action}");

        await EmitEventAsync(new OnAgentStart
        {
            TaskId = context.TaskId,
            Action = step.Action,
            Parameters = step.Parameters
        });

        try
        {
            var result = await ExecuteTaskAsync(step, context, cancellationToken);

            await EmitEventAsync(new OnAgentResult
            {
                TaskId = context.TaskId,
                Result = result,
                Success = true,
                Duration = DateTime.UtcNow - startTime
            });

            LogStep(context, $"? {Name} concluído com sucesso");

            return new AgentResult
            {
                AgentName = Name,
                Result = result,
                Success = true,
                Timestamp = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            await EmitEventAsync(new OnAgentError
            {
                TaskId = context.TaskId,
                Error = ex.Message,
                WillRetry = step.RetryCount < step.MaxRetries
            });

            LogStep(context, $"? Erro no {Name}: {ex.Message}");

            return new AgentResult
            {
                AgentName = Name,
                Success = false,
                Error = ex.Message,
                Timestamp = DateTime.UtcNow
            };
        }
    }

    private async Task<string> ExecuteTaskAsync(
        TaskStep step,
        TaskExecutionContext context,
        CancellationToken cancellationToken)
    {
        var prompt = $@"
Você é um worker especializado: {_workerDescription}

CAPACIDADES: {string.Join(", ", _workerCapabilities)}

TAREFA: {step.Action}
PARÂMETROS: {System.Text.Json.JsonSerializer.Serialize(step.Parameters)}
CONTEXTO: {context.Prompt}

Execute a tarefa e forneça o resultado.
";

        return await CallLLMWithRetryAsync(prompt, cancellationToken: cancellationToken);
    }
}
