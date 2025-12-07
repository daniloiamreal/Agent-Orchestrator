using Agent.Orchestrator.Api.DTOs;

namespace Agent.Orchestrator.Api.Services;

/// <summary>
/// Interface do barramento de eventos para comunicação entre agentes e SignalR
/// </summary>
public interface IAgentEventBus
{
    /// <summary>
    /// Publica um evento
    /// </summary>
    Task PublishAsync(AgentEvent agentEvent);
    
    /// <summary>
    /// Subscreve a eventos de um tipo específico
    /// </summary>
    IDisposable Subscribe<T>(Func<T, Task> handler) where T : AgentEvent;
    
    /// <summary>
    /// Subscreve a todos os eventos
    /// </summary>
    IDisposable SubscribeAll(Func<AgentEvent, Task> handler);
}
