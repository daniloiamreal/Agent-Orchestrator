using Agent.Orchestrator.Api.Services;
using Agent.Orchestrator.Api.DTOs;

namespace Agent.Orchestrator.Api.Agents;

public class ReviewerAgent
{
    private readonly ILLMService _llmService;
    private readonly IWorkspaceService _workspaceService;

    public ReviewerAgent(ILLMService llmService, IWorkspaceService workspaceService)
    {
        _llmService = llmService;
        _workspaceService = workspaceService;
    }

    public async Task<string> ExecuteAsync(TaskExecutionContext context, string fileName)
    {
        LogStep(context, "🔍 ReviewerAgent iniciado...");
        LogStep(context, $"📂 Lendo arquivo: {fileName}");

        string content;
        try
        {
            content = await _workspaceService.ReadFileAsync(fileName);
        }
        catch
        {
            content = context.GeneratedCode ?? "Nenhum conteúdo para revisar";
        }

        LogStep(context, "🧐 Analisando conteúdo com IA...");

        var prompt = $@"
Você é um revisor especializado.
Analise o conteúdo abaixo e forneça feedback detalhado:

Conteúdo para revisão:
{content}

Forneça:
1. Pontos positivos
2. Pontos a melhorar
3. Sugestões específicas
4. Avaliação geral (nota de 0 a 10)

Seja construtivo e específico.
";

        var reviewResult = await _llmService.GenerateResponseAsync(prompt);
        context.ReviewResult = reviewResult;

        // Exibir resultado nos logs
        LogStep(context, "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        LogStep(context, "📊 RESULTADO DA REVISÃO:");
        LogStep(context, "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        
        var lines = reviewResult.Split('\n');
        foreach (var line in lines)
        {
            LogStep(context, line);
        }
        
        LogStep(context, "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");

        return reviewResult;
    }

    private void LogStep(TaskExecutionContext context, string message)
    {
        context.Logs.Enqueue(message);
    }
}