using Agent.Orchestrator.Api.Services;
using Agent.Orchestrator.Api.DTOs;

namespace Agent.Orchestrator.Api.Agents;

public class CodeGeneratorAgent
{
    private readonly ILLMService _llmService;
    private readonly IWorkspaceService _workspaceService;

    public CodeGeneratorAgent(ILLMService llmService, IWorkspaceService workspaceService)
    {
        _llmService = llmService;
        _workspaceService = workspaceService;
    }

    public async Task<string> ExecuteAsync(TaskExecutionContext context)
    {
        try
        {
            LogStep(context, "🤖 CodeGeneratorAgent iniciado...");
            LogStep(context, "💭 Analisando requisitos do usuário...");

            var prompt = $@"
Você é um assistente criativo e inteligente.
Tarefa do usuário: {context.Prompt}

Execute a tarefa solicitada de forma completa e detalhada.
Se for código, gere código funcional.
Se for texto criativo (poesia, história, etc.), seja criativo e eloquente.
";

            LogStep(context, "⚙️ Processando com IA (Perplexity)...");
            
            string response;
            try
            {
                response = await _llmService.GenerateResponseAsync(prompt);
                LogStep(context, "✅ Resposta recebida da IA");
            }
            catch (Exception ex)
            {
                LogStep(context, $"❌ Erro ao chamar LLM: {ex.Message}");
                throw;
            }

            // Determinar tipo de arquivo baseado no conteúdo
            var isCode = response.Contains("class ") || response.Contains("public ") || 
                        response.Contains("function ") || response.Contains("def ") ||
                        response.Contains("```");
            
            var fileName = isCode ? "GeneratedCode.cs" : "GeneratedContent.txt";
            
            try
            {
                await _workspaceService.SaveFileAsync(fileName, response);
                LogStep(context, $"💾 Conteúdo salvo em: {fileName}");
            }
            catch (Exception ex)
            {
                LogStep(context, $"❌ Erro ao salvar arquivo: {ex.Message}");
                throw;
            }
            
            context.GeneratedCode = response;

            // Exibir resposta nos logs de forma visível
            LogStep(context, "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            LogStep(context, "📝 RESPOSTA DA IA:");
            LogStep(context, "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            
            // Quebrar resposta em linhas para melhor visualização
            var lines = response.Split('\n');
            foreach (var line in lines)
            {
                LogStep(context, line);
            }
            
            LogStep(context, "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");

            return fileName;
        }
        catch (Exception ex)
        {
            LogStep(context, $"❌ ERRO CRÍTICO no CodeGeneratorAgent: {ex.Message}");
            throw;
        }
    }

    private void LogStep(TaskExecutionContext context, string message)
    {
        context.Logs.Enqueue(message);
    }
}