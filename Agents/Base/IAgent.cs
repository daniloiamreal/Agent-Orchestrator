using Agent.Orchestrator.Api.DTOs;

namespace Agent.Orchestrator.Api.Agents.Base;

/// <summary>
/// Interface base para todos os agentes do sistema
/// </summary>
public interface IAgent
{
    /// <summary>
    /// Nome único do agente
    /// </summary>
    string Name { get; }
    
    /// <summary>
    /// Descrição das capacidades do agente
    /// </summary>
    string Description { get; }
    
    /// <summary>
    /// Lista de capacidades/skills do agente
    /// </summary>
    IReadOnlyList<string> Capabilities { get; }
    
    /// <summary>
    /// Executa uma ação específica
    /// </summary>
    Task<AgentResult> ExecuteAsync(
        TaskStep step, 
        TaskExecutionContext context, 
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Verifica se o agente pode executar determinada ação
    /// </summary>
    bool CanHandle(string action);
    
    /// <summary>
    /// Valida os parâmetros antes da execução
    /// </summary>
    Task<bool> ValidateAsync(TaskStep step, TaskExecutionContext context);
}
