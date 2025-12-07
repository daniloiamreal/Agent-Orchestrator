using Agent.Orchestrator.Api.DTOs;

namespace Agent.Orchestrator.Api.NLP;

/// <summary>
/// Interface para processamento de linguagem natural
/// </summary>
public interface INLPInterface
{
    /// <summary>
    /// Analisa o prompt do usuário e extrai intenções estruturadas
    /// </summary>
    Task<IntentResult> ParseIntentAsync(string userInput, CancellationToken cancellationToken = default);
}
