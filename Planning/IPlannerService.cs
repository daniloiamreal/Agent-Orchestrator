using Agent.Orchestrator.Api.DTOs;

namespace Agent.Orchestrator.Api.Planning;

/// <summary>
/// Interface do serviço de planejamento
/// </summary>
public interface IPlannerService
{
    /// <summary>
    /// Cria um plano de execução baseado na intenção
    /// </summary>
    Task<ExecutionPlan> CreatePlanAsync(
        IntentResult intent, 
        TaskExecutionContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Replanning quando ocorre falha ou mudança de contexto
    /// </summary>
    Task<ExecutionPlan> ReplanAsync(
        ExecutionPlan currentPlan,
        string reason,
        TaskExecutionContext context,
        CancellationToken cancellationToken = default);
}
