using Agent.Orchestrator.Api.Agents.Base;
using Agent.Orchestrator.Api.DTOs;
using Agent.Orchestrator.Api.Services;
using System.Text;
using System.Text.Json;

namespace Agent.Orchestrator.Api.Agents.Specialized;

/// <summary>
/// Agente de integração com APIs externas
/// Pode fazer chamadas HTTP reais e gerar código de integração
/// </summary>
public class APIIntegrationAgent : BaseAgent
{
    private readonly HttpClient _httpClient;

    public override string Name => "APIIntegrationAgent";
    public override string Description => "Integra com APIs externas, faz requisições HTTP e gera código de integração";
    public override IReadOnlyList<string> Capabilities => new[]
    {
        "call-api",
        "fetch-data",
        "send-request",
        "parse-response",
        "generate-client",
        "test-endpoint"
    };

    public APIIntegrationAgent(
        ILLMService llmService,
        IWorkspaceService workspaceService,
        IAgentEventBus eventBus,
        ISharedMemory memory,
        ILogger<APIIntegrationAgent> logger)
        : base(llmService, workspaceService, eventBus, memory, logger)
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
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
            string result = step.Action.ToLower() switch
            {
                "call-api" or "fetch-data" => await CallAPIAsync(step, context, cancellationToken),
                "send-request" => await SendRequestAsync(step, context, cancellationToken),
                "generate-client" => await GenerateAPIClientAsync(step, context, cancellationToken),
                "test-endpoint" => await TestEndpointAsync(step, context, cancellationToken),
                _ => await ProcessAPITaskAsync(step, context, cancellationToken)
            };

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

    /// <summary>
    /// Faz chamada real a uma API
    /// </summary>
    private async Task<string> CallAPIAsync(TaskStep step, TaskExecutionContext context, CancellationToken cancellationToken)
    {
        var url = step.Parameters.TryGetValue("url", out var u) ? u.ToString() : null;
        var method = step.Parameters.TryGetValue("method", out var m) ? m.ToString()?.ToUpper() : "GET";

        if (string.IsNullOrEmpty(url))
        {
            // Se não tem URL, gerar código de exemplo
            return await GenerateAPIClientAsync(step, context, cancellationToken);
        }

        LogStep(context, $"?? Chamando API: {method} {url}");

        try
        {
            HttpResponseMessage response;
            
            switch (method)
            {
                case "POST":
                    var postBody = step.Parameters.TryGetValue("body", out var pb) ? pb.ToString() : "{}";
                    var postContent = new StringContent(postBody!, System.Text.Encoding.UTF8, "application/json");
                    response = await _httpClient.PostAsync(url, postContent, cancellationToken);
                    break;
                case "PUT":
                    var putBody = step.Parameters.TryGetValue("body", out var pub) ? pub.ToString() : "{}";
                    var putContent = new StringContent(putBody!, System.Text.Encoding.UTF8, "application/json");
                    response = await _httpClient.PutAsync(url, putContent, cancellationToken);
                    break;
                case "DELETE":
                    response = await _httpClient.DeleteAsync(url, cancellationToken);
                    break;
                default:
                    response = await _httpClient.GetAsync(url, cancellationToken);
                    break;
            }

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            var statusCode = (int)response.StatusCode;

            LogStep(context, $"?? Resposta: {statusCode} ({response.StatusCode})");

            // Formatar resposta
            var result = new StringBuilder();
            result.AppendLine($"## Resultado da Chamada API");
            result.AppendLine($"**URL:** {url}");
            result.AppendLine($"**Método:** {method}");
            result.AppendLine($"**Status:** {statusCode} ({response.StatusCode})");
            result.AppendLine();
            result.AppendLine("**Resposta:**");
            result.AppendLine("```json");
            
            // Tentar formatar JSON
            try
            {
                var jsonDoc = JsonDocument.Parse(responseBody);
                result.AppendLine(JsonSerializer.Serialize(jsonDoc.RootElement, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch
            {
                result.AppendLine(responseBody.Length > 1000 ? responseBody.Substring(0, 1000) + "..." : responseBody);
            }
            
            result.AppendLine("```");

            context.SharedState["APIResponse"] = responseBody;
            context.SharedState["APIStatusCode"] = statusCode;

            return result.ToString();
        }
        catch (HttpRequestException ex)
        {
            return $"? Erro na chamada API: {ex.Message}\n\nSugestão: Verifique se a URL está correta e acessível.";
        }
    }

    /// <summary>
    /// Envia requisição customizada
    /// </summary>
    private async Task<string> SendRequestAsync(TaskStep step, TaskExecutionContext context, CancellationToken cancellationToken)
    {
        return await CallAPIAsync(step, context, cancellationToken);
    }

    /// <summary>
    /// Gera código cliente para API
    /// </summary>
    private async Task<string> GenerateAPIClientAsync(TaskStep step, TaskExecutionContext context, CancellationToken cancellationToken)
    {
        var apiDescription = step.Parameters.TryGetValue("api", out var api) ? api.ToString() : context.Prompt;
        var language = step.Parameters.TryGetValue("language", out var lang) ? lang.ToString() : "C#";

        LogStep(context, $"?? Gerando cliente {language} para API...");

        var prompt = $@"
Você é um especialista em integração de APIs.

TAREFA: Gerar código cliente para integração com API
DESCRIÇÃO DA API: {apiDescription}
LINGUAGEM: {language}

Gere um código completo e funcional que inclua:
1. Classe cliente com métodos para cada endpoint
2. Modelos de dados (DTOs)
3. Tratamento de erros
4. Exemplos de uso
5. Documentação inline

Use boas práticas como:
- HttpClientFactory (para C#)
- Retry policies
- Logging
- Tipagem forte
";

        var code = await CallLLMWithRetryAsync(prompt, cancellationToken: cancellationToken);
        
        // Salvar no workspace
        var fileName = $"APIClient_{DateTime.Now:yyyyMMdd_HHmmss}.cs";
        await _workspaceService.SaveFileAsync(fileName, code);
        
        context.SharedState["GeneratedAPIClient"] = code;
        LogStep(context, $"?? Código salvo em: {fileName}");

        return code;
    }

    /// <summary>
    /// Testa um endpoint de API
    /// </summary>
    private async Task<string> TestEndpointAsync(TaskStep step, TaskExecutionContext context, CancellationToken cancellationToken)
    {
        var url = step.Parameters.TryGetValue("url", out var u) ? u.ToString() : null;

        if (string.IsNullOrEmpty(url))
        {
            return "? URL não fornecida para teste. Especifique o parâmetro 'url'.";
        }

        LogStep(context, $"?? Testando endpoint: {url}");

        var results = new StringBuilder();
        results.AppendLine($"## Teste de Endpoint: {url}");
        results.AppendLine();

        // Teste de conectividade
        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var response = await _httpClient.GetAsync(url, cancellationToken);
            stopwatch.Stop();

            results.AppendLine($"? **Conectividade:** OK");
            results.AppendLine($"?? **Tempo de resposta:** {stopwatch.ElapsedMilliseconds}ms");
            results.AppendLine($"?? **Status Code:** {(int)response.StatusCode} ({response.StatusCode})");
            results.AppendLine($"?? **Content-Type:** {response.Content.Headers.ContentType}");
            results.AppendLine($"?? **Content-Length:** {response.Content.Headers.ContentLength ?? 0} bytes");
            results.AppendLine();

            // Headers de segurança
            results.AppendLine("**Headers de Segurança:**");
            var securityHeaders = new[] { "X-Frame-Options", "X-Content-Type-Options", "Strict-Transport-Security", "X-XSS-Protection" };
            foreach (var header in securityHeaders)
            {
                var hasHeader = response.Headers.Contains(header);
                results.AppendLine($"- {header}: {(hasHeader ? "? Presente" : "?? Ausente")}");
            }

            // Amostra da resposta
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            results.AppendLine();
            results.AppendLine("**Amostra da Resposta:**");
            results.AppendLine("```");
            results.AppendLine(body.Length > 500 ? body.Substring(0, 500) + "..." : body);
            results.AppendLine("```");
        }
        catch (Exception ex)
        {
            results.AppendLine($"? **Erro:** {ex.Message}");
        }

        return results.ToString();
    }

    /// <summary>
    /// Processa tarefa genérica de API
    /// </summary>
    private async Task<string> ProcessAPITaskAsync(TaskStep step, TaskExecutionContext context, CancellationToken cancellationToken)
    {
        var prompt = $@"
Você é um especialista em APIs REST e integração de sistemas.

TAREFA: {step.Action}
CONTEXTO: {context.Prompt}
PARÂMETROS: {JsonSerializer.Serialize(step.Parameters)}

Forneça:
1. Análise da tarefa
2. Solução técnica detalhada
3. Código de exemplo se aplicável
4. Considerações de segurança
5. Boas práticas
";

        var result = await CallLLMWithRetryAsync(prompt, cancellationToken: cancellationToken);
        context.SharedState["APIIntegrationResult"] = result;
        
        return result;
    }
}
