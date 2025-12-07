namespace Agent.Orchestrator.Api.Services;

public interface ILLMService
{
    Task<string> GenerateResponseAsync(string prompt, CancellationToken cancellationToken = default);
}