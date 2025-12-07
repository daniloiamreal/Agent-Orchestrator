using Agent.Orchestrator.Api.DTOs;
using System.Collections.Concurrent;

namespace Agent.Orchestrator.Api.Services;

/// <summary>
/// Implementação do barramento de eventos
/// </summary>
public class AgentEventBus : IAgentEventBus
{
    private readonly ConcurrentDictionary<Type, List<Delegate>> _handlers = new();
    private readonly List<Func<AgentEvent, Task>> _allHandlers = new();
    private readonly ILogger<AgentEventBus> _logger;
    private readonly object _lock = new();

    public AgentEventBus(ILogger<AgentEventBus> logger)
    {
        _logger = logger;
    }

    public async Task PublishAsync(AgentEvent agentEvent)
    {
        _logger.LogDebug("Publishing event: {EventType} for task {TaskId}", 
            agentEvent.EventType, agentEvent.TaskId);

        // Notifica handlers específicos
        var eventType = agentEvent.GetType();
        if (_handlers.TryGetValue(eventType, out var handlers))
        {
            foreach (var handler in handlers.ToList())
            {
                try
                {
                    var task = (Task?)handler.DynamicInvoke(agentEvent);
                    if (task != null) await task;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in event handler for {EventType}", eventType.Name);
                }
            }
        }

        // Notifica handlers globais
        foreach (var handler in _allHandlers.ToList())
        {
            try
            {
                await handler(agentEvent);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in global event handler");
            }
        }
    }

    public IDisposable Subscribe<T>(Func<T, Task> handler) where T : AgentEvent
    {
        var eventType = typeof(T);
        
        lock (_lock)
        {
            if (!_handlers.ContainsKey(eventType))
            {
                _handlers[eventType] = new List<Delegate>();
            }
            _handlers[eventType].Add(handler);
        }

        return new Subscription(() =>
        {
            lock (_lock)
            {
                if (_handlers.TryGetValue(eventType, out var h))
                {
                    h.Remove(handler);
                }
            }
        });
    }

    public IDisposable SubscribeAll(Func<AgentEvent, Task> handler)
    {
        lock (_lock)
        {
            _allHandlers.Add(handler);
        }

        return new Subscription(() =>
        {
            lock (_lock)
            {
                _allHandlers.Remove(handler);
            }
        });
    }

    private class Subscription : IDisposable
    {
        private readonly Action _unsubscribe;

        public Subscription(Action unsubscribe)
        {
            _unsubscribe = unsubscribe;
        }

        public void Dispose() => _unsubscribe();
    }
}
