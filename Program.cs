using Agent.Orchestrator.Api.Services;
using Agent.Orchestrator.Api.Agents;
using Agent.Orchestrator.Api.DTOs;
using Agent.Orchestrator.Api.Hubs;
using Agent.Orchestrator.Api.NLP;
using Agent.Orchestrator.Api.Planning;
using System.Collections.Concurrent;

var builder = WebApplication.CreateBuilder(args);

// ============================================
// SERVIÇOS BASE
// ============================================
builder.Services.AddSingleton<ILLMService, LLMService>();
builder.Services.AddSingleton<IWorkspaceService, WorkspaceService>();

// ============================================
// SERVIÇOS DO SISTEMA DE AGENTES AUTÔNOMOS
// ============================================
builder.Services.AddSingleton<IAgentEventBus, AgentEventBus>();
builder.Services.AddSingleton<ISharedMemory, InMemorySharedMemory>();
builder.Services.AddSingleton<INLPInterface, IntentParser>();
builder.Services.AddSingleton<IPlannerService, LLMPlanner>();

// ============================================
// ORQUESTRADOR E CONTEXTOS
// ============================================
builder.Services.AddSingleton<AgentOrchestrator>();
builder.Services.AddSingleton<ConcurrentDictionary<string, TaskExecutionContext>>();

// ============================================
// SIGNALR
// ============================================
builder.Services.AddSignalR();
builder.Services.AddSingleton<AgentHubNotifier>();

// ============================================
// CORS (configurado para SignalR)
// ============================================
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
    
    // Policy específica para SignalR
    options.AddPolicy("SignalR", policy =>
    {
        policy.WithOrigins("http://localhost:5130", "http://localhost:8080", "http://127.0.0.1:5130")
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

var app = builder.Build();

// Configurar Event Bus para enviar eventos via SignalR
var eventBus = app.Services.GetRequiredService<IAgentEventBus>();
var hubNotifier = app.Services.GetRequiredService<AgentHubNotifier>();
eventBus.SubscribeAll(async evt => await hubNotifier.SendEventAsync(evt));

// IMPORTANTE: A ORDEM DOS MIDDLEWARES IMPORTA!
app.UseCors();

// 1. Primeiro UseDefaultFiles (index.html)
app.UseDefaultFiles();

// 2. Depois UseStaticFiles (CSS, JS, etc)
app.UseStaticFiles();

// Log para debug
app.Logger.LogInformation("📁 Servindo arquivos estáticos de: {Path}", 
    Path.Combine(app.Environment.ContentRootPath, "wwwroot"));

// ============================================
// SIGNALR HUB
// ============================================
app.MapHub<AgentHub>("/hubs/agent").RequireCors("SignalR");

// ============================================
// ENDPOINTS DA API
// ============================================

// Endpoint legado (compatibilidade)
app.MapPost("/run-task", async (TaskRequest request, AgentOrchestrator orchestrator, ConcurrentDictionary<string, TaskExecutionContext> contexts) =>
{
    var taskId = Guid.NewGuid().ToString();
    var context = new TaskExecutionContext
    {
        TaskId = taskId,
        Prompt = request.Prompt,
        Logs = new ConcurrentQueue<string>()
    };

    contexts[taskId] = context;

    _ = Task.Run(async () =>
    {
        try
        {
            await orchestrator.ExecuteAsync(context);
        }
        catch (Exception ex)
        {
            context.Logs.Enqueue($"❌ ERRO: {ex.Message}");
            context.IsCompleted = true;
        }
    });

    return Results.Ok(new { taskId });
});

// Novo endpoint para agentes autônomos
app.MapPost("/run-autonomous", async (TaskRequest request, AgentOrchestrator orchestrator, ConcurrentDictionary<string, TaskExecutionContext> contexts) =>
{
    var taskId = Guid.NewGuid().ToString();
    var context = new TaskExecutionContext
    {
        TaskId = taskId,
        Prompt = request.Prompt,
        Logs = new ConcurrentQueue<string>()
    };

    contexts[taskId] = context;

    _ = Task.Run(async () =>
    {
        try
        {
            await orchestrator.ExecuteAutonomousAsync(context);
        }
        catch (Exception ex)
        {
            context.Logs.Enqueue($"❌ ERRO: {ex.Message}");
            context.IsCompleted = true;
        }
    });

    return Results.Ok(new { taskId, mode = "autonomous" });
});

// Endpoint para obter status da tarefa
app.MapGet("/task/{taskId}/status", (string taskId, ConcurrentDictionary<string, TaskExecutionContext> contexts) =>
{
    if (!contexts.TryGetValue(taskId, out var context))
    {
        return Results.NotFound(new { error = "Task não encontrada" });
    }

    return Results.Ok(new
    {
        taskId = context.TaskId,
        status = context.Status.ToString(),
        isCompleted = context.IsCompleted,
        progress = context.Plan?.Progress ?? 0,
        replanCount = context.ReplanCount,
        startedAt = context.StartedAt,
        completedAt = context.CompletedAt,
        errors = context.Errors
    });
});

// Endpoint para obter plano de execução
app.MapGet("/task/{taskId}/plan", (string taskId, ConcurrentDictionary<string, TaskExecutionContext> contexts) =>
{
    if (!contexts.TryGetValue(taskId, out var context))
    {
        return Results.NotFound(new { error = "Task não encontrada" });
    }

    if (context.Plan == null)
    {
        return Results.Ok(new { message = "Plano ainda não foi criado" });
    }

    return Results.Ok(new
    {
        planId = context.Plan.PlanId,
        objective = context.Plan.Objective,
        mode = context.Plan.Mode.ToString(),
        version = context.Plan.Version,
        isComplete = context.Plan.IsComplete,
        progress = context.Plan.Progress,
        steps = context.Plan.Steps.Select(s => new
        {
            stepId = s.StepId,
            order = s.Order,
            agentName = s.AgentName,
            action = s.Action,
            status = s.Status.ToString(),
            result = s.Result,
            error = s.Error
        })
    });
});

// Endpoint para cancelar tarefa
app.MapPost("/task/{taskId}/cancel", (string taskId, ConcurrentDictionary<string, TaskExecutionContext> contexts) =>
{
    if (!contexts.TryGetValue(taskId, out var context))
    {
        return Results.NotFound(new { error = "Task não encontrada" });
    }

    context.CancellationTokenSource.Cancel();
    context.Status = ExecutionStatus.Cancelled;
    context.Logs.Enqueue("⚠️ Tarefa cancelada pelo usuário");

    return Results.Ok(new { message = "Tarefa cancelada" });
});

// Stream SSE (compatibilidade)
app.MapGet("/stream/{taskId}", async (string taskId, ConcurrentDictionary<string, TaskExecutionContext> contexts, HttpContext httpContext) =>
{
    if (!contexts.TryGetValue(taskId, out var context))
    {
        httpContext.Response.StatusCode = 404;
        await httpContext.Response.WriteAsJsonAsync(new { error = "Task não encontrada" });
        return;
    }

    httpContext.Response.ContentType = "text/event-stream; charset=utf-8";
    httpContext.Response.Headers["Cache-Control"] = "no-cache";
    httpContext.Response.Headers["Connection"] = "keep-alive";

    try
    {
        // Enviar logs enquanto a tarefa não estiver completa
        while (!context.IsCompleted || !context.Logs.IsEmpty)
        {
            // Processar todos os logs disponíveis
            while (context.Logs.TryDequeue(out var log))
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes($"data: {log}\n\n");
                await httpContext.Response.Body.WriteAsync(bytes);
                await httpContext.Response.Body.FlushAsync();
            }

            // Aguardar um pouco antes de verificar novamente
            if (!context.IsCompleted)
            {
                await Task.Delay(100);
            }
        }

        // Sinal de conclusão
        var doneBytes = System.Text.Encoding.UTF8.GetBytes("data: [DONE]\n\n");
        await httpContext.Response.Body.WriteAsync(doneBytes);
        await httpContext.Response.Body.FlushAsync();
    }
    catch (Exception)
    {
        // Cliente desconectou, isso é normal para SSE
    }
});

// Health check
app.MapGet("/health", () => Results.Ok(new { 
    status = "healthy", 
    timestamp = DateTime.UtcNow,
    version = "2.0.0",
    features = new[] { "autonomous-agents", "signalr", "planner", "memory" }
}));

// Teste de conexão com LLM
app.MapGet("/test-llm", async (ILLMService llmService, IConfiguration config) =>
{
    var provider = config["AI:Provider"] ?? "Unknown";
    var model = config["AI:Model"] ?? "Unknown";
    var useMock = config.GetValue<bool>("UseMockLLM");

    try
    {
        var startTime = DateTime.UtcNow;
        var response = await llmService.GenerateResponseAsync("Responda apenas: OK - Conexão estabelecida com sucesso!");
        var duration = DateTime.UtcNow - startTime;

        return Results.Ok(new
        {
            success = true,
            provider,
            model,
            useMock,
            responseTime = $"{duration.TotalMilliseconds:F0}ms",
            response = response.Length > 200 ? response.Substring(0, 200) + "..." : response
        });
    }
    catch (Exception ex)
    {
        return Results.Ok(new
        {
            success = false,
            provider,
            model,
            useMock,
            error = ex.Message
        });
    }
});

// Teste completo de todos os agentes
app.MapGet("/test-agents", async (ILLMService llmService, IConfiguration config) =>
{
    var results = new List<object>();
    var provider = config["AI:Provider"] ?? "Unknown";
    var useMock = config.GetValue<bool>("UseMockLLM");

    var tests = new[]
    {
        ("LLM Básico", "Diga apenas: Teste OK"),
        ("CodeGenerator", "Gere uma função simples em C# que soma dois números. Responda apenas com o código."),
        ("Reviewer", "Revise este código: public int Add(int a, int b) => a + b; - Responda de forma breve."),
        ("IntentParser", "Você é um analisador de intenções. COMANDO DO USUÁRIO: criar calculadora. Retorne um JSON simples."),
        ("Planner", "Você é um planejador de tarefas. AGENTES DISPONÍVEIS: CodeGenerator, Reviewer. Crie um plano simples em JSON.")
    };

    foreach (var (name, prompt) in tests)
    {
        try
        {
            var startTime = DateTime.UtcNow;
            var response = await llmService.GenerateResponseAsync(prompt);
            var duration = DateTime.UtcNow - startTime;

            results.Add(new
            {
                agent = name,
                success = true,
                responseTime = $"{duration.TotalMilliseconds:F0}ms",
                responseLength = response.Length,
                preview = response.Length > 100 ? response.Substring(0, 100) + "..." : response
            });
        }
        catch (Exception ex)
        {
            results.Add(new
            {
                agent = name,
                success = false,
                error = ex.Message
            });
        }
    }

    var successCount = results.Count(r => ((dynamic)r).success == true);

    return Results.Ok(new
    {
        summary = new
        {
            provider,
            useMock,
            totalTests = tests.Length,
            passed = successCount,
            failed = tests.Length - successCount,
            allPassed = successCount == tests.Length
        },
        results
    });
});

// Info dos agentes disponíveis
app.MapGet("/agents", () => Results.Ok(new
{
    agents = new[]
    {
        new { name = "CodeGeneratorAgent", description = "Gera código em C#, Python, JavaScript, etc.", capabilities = new[] { "generate-code", "create-class", "implement-interface" } },
        new { name = "ReviewerAgent", description = "Revisa código e fornece feedback", capabilities = new[] { "review-code", "analyze-quality", "suggest-improvements" } },
        new { name = "RAGAgent", description = "Busca em documentos e bases de conhecimento", capabilities = new[] { "search-documents", "retrieve-context", "semantic-search", "upload-document" } },
        new { name = "APIIntegrationAgent", description = "Integra com APIs externas", capabilities = new[] { "call-api", "fetch-data", "send-request", "test-endpoint" } },
        new { name = "WorkflowAgent", description = "Executa automações e pipelines", capabilities = new[] { "run-script", "execute-pipeline", "automate-task", "create-workflow" } },
        new { name = "AnalystAgent", description = "Análise de dados e decisões", capabilities = new[] { "analyze-data", "make-decision", "validate-logic", "generate-report" } },
        new { name = "SupervisorAgent", description = "Coordena múltiplos agentes", capabilities = new[] { "coordinate-agents", "validate-results", "manage-workflow", "quality-control" } }
    }
}));

// ============================================
// ENDPOINTS RAG (Upload de Documentos)
// ============================================

// Upload de documento para RAG (suporta txt, md, json, csv, html, xml, pdf)
app.MapPost("/rag/upload", async (HttpRequest request) =>
{
    try
    {
        var form = await request.ReadFormAsync();
        var file = form.Files.FirstOrDefault();
        var sessionId = form["sessionId"].FirstOrDefault() ?? Guid.NewGuid().ToString();

        if (file == null || file.Length == 0)
        {
            return Results.BadRequest(new { error = "Nenhum arquivo enviado" });
        }

        string content;
        var extension = Path.GetExtension(file.FileName).ToLower();

        // Processar PDF
        if (extension == ".pdf")
        {
            content = ExtractTextFromPdf(file.OpenReadStream());
            if (string.IsNullOrWhiteSpace(content))
            {
                return Results.BadRequest(new { error = "Não foi possível extrair texto do PDF. O arquivo pode estar vazio ou protegido." });
            }
        }
        else
        {
            // Ler conteúdo de arquivos de texto
            using var reader = new StreamReader(file.OpenReadStream());
            content = await reader.ReadToEndAsync();
        }

        // Adicionar ao RAG
        Agent.Orchestrator.Api.Agents.Specialized.RAGAgent.AddDocument(sessionId, content, file.FileName);

        // Obter estatísticas
        var stats = Agent.Orchestrator.Api.Agents.Specialized.RAGAgent.GetStats(sessionId);

        return Results.Ok(new
        {
            success = true,
            message = $"Documento '{file.FileName}' carregado com sucesso!",
            sessionId,
            fileName = file.FileName,
            fileSize = file.Length,
            fileType = extension,
            extractedCharacters = content.Length,
            stats = new
            {
                totalChunks = stats.TotalChunks,
                totalCharacters = stats.TotalCharacters,
                sources = stats.Sources
            }
        });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

// Função para extrair texto de PDF
static string ExtractTextFromPdf(Stream pdfStream)
{
    var text = new System.Text.StringBuilder();
    
    try
    {
        using var document = UglyToad.PdfPig.PdfDocument.Open(pdfStream);
        
        foreach (var page in document.GetPages())
        {
            var pageText = page.Text;
            if (!string.IsNullOrWhiteSpace(pageText))
            {
                text.AppendLine($"--- Página {page.Number} ---");
                text.AppendLine(pageText);
                text.AppendLine();
            }
        }
    }
    catch (Exception ex)
    {
        throw new Exception($"Erro ao processar PDF: {ex.Message}");
    }
    
    return text.ToString();
}

// Upload de texto direto para RAG
app.MapPost("/rag/upload-text", async (RagTextUploadRequest request) =>
{
    try
    {
        if (string.IsNullOrWhiteSpace(request.Content))
        {
            return Results.BadRequest(new { error = "Conteúdo vazio" });
        }

        var sessionId = request.SessionId ?? Guid.NewGuid().ToString();
        var fileName = request.FileName ?? $"text_{DateTime.Now:yyyyMMdd_HHmmss}.txt";

        Agent.Orchestrator.Api.Agents.Specialized.RAGAgent.AddDocument(sessionId, request.Content, fileName);

        var stats = Agent.Orchestrator.Api.Agents.Specialized.RAGAgent.GetStats(sessionId);

        return Results.Ok(new
        {
            success = true,
            message = $"Texto carregado como '{fileName}'",
            sessionId,
            stats = new
            {
                totalChunks = stats.TotalChunks,
                totalCharacters = stats.TotalCharacters,
                sources = stats.Sources
            }
        });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

// Obter estatísticas do RAG
app.MapGet("/rag/stats", (string? sessionId) =>
{
    var stats = Agent.Orchestrator.Api.Agents.Specialized.RAGAgent.GetStats(sessionId ?? "global");
    return Results.Ok(new
    {
        totalChunks = stats.TotalChunks,
        totalCharacters = stats.TotalCharacters,
        sources = stats.Sources,
        hasDocuments = stats.TotalChunks > 0
    });
});

// Limpar documentos do RAG
app.MapDelete("/rag/clear", (string? sessionId) =>
{
    Agent.Orchestrator.Api.Agents.Specialized.RAGAgent.ClearDocuments(sessionId ?? "global");
    return Results.Ok(new { message = "Documentos removidos", sessionId });
});

// ============================================
// CONSULTA RAG EXCLUSIVA (direto aos documentos)
// ============================================
app.MapPost("/rag/query", async (RagQueryRequest request, ILLMService llmService) =>
{
    try
    {
        var sessionId = request.SessionId ?? "global";
        var query = request.Query;

        if (string.IsNullOrWhiteSpace(query))
        {
            return Results.BadRequest(new { success = false, error = "Pergunta não pode estar vazia" });
        }

        // Obter estatísticas para verificar se há documentos
        var stats = Agent.Orchestrator.Api.Agents.Specialized.RAGAgent.GetStats(sessionId);
        
        if (stats.TotalChunks == 0)
        {
            return Results.Ok(new
            {
                success = false,
                error = "Nenhum documento carregado. Faça upload de um documento primeiro.",
                sources = Array.Empty<string>()
            });
        }

        // Buscar chunks relevantes usando o método estático
        var chunks = Agent.Orchestrator.Api.Agents.Specialized.RAGAgent.GetAllChunks(sessionId);
        
        if (!chunks.Any())
        {
            return Results.Ok(new
            {
                success = false,
                error = "Nenhum documento encontrado para consulta.",
                sources = Array.Empty<string>()
            });
        }

        // Preparar previews dos chunks para ranking
        var chunkPreviews = chunks
            .Select((c, i) => $"[{i}] ({c.Source}): {c.Content.Substring(0, Math.Min(c.Content.Length, 250))}")
            .Take(20)
            .ToList();

        // Usar LLM para ranquear chunks por relevância
        var rankingPrompt = $@"
Você é um sistema de ranking de relevância para RAG (Retrieval-Augmented Generation).

PERGUNTA DO USUÁRIO: {query}

CHUNKS DE DOCUMENTOS DISPONÍVEIS:
{string.Join("\n---\n", chunkPreviews)}

TAREFA: Identifique os 3 chunks MAIS RELEVANTES para responder a pergunta.

Retorne APENAS os números dos índices, separados por vírgula.
Exemplo de resposta: 0, 3, 7
";

        var rankingResult = await llmService.GenerateResponseAsync(rankingPrompt);
        
        // Extrair índices
        var indices = System.Text.RegularExpressions.Regex.Matches(rankingResult, @"\d+")
            .Select(m => int.TryParse(m.Value, out var i) ? i : -1)
            .Where(i => i >= 0 && i < chunks.Count)
            .Distinct()
            .Take(3)
            .ToList();

        var selectedChunks = indices.Any() 
            ? indices.Select(i => chunks[i]).ToList()
            : chunks.Take(3).ToList();

        var usedSources = selectedChunks.Select(c => c.Source).Distinct().ToList();

        // Gerar resposta baseada EXCLUSIVAMENTE nos documentos
        var answerPrompt = $@"
Você é um assistente que responde perguntas EXCLUSIVAMENTE com base nos documentos fornecidos.

PERGUNTA: {query}

📄 CONTEÚDO DOS DOCUMENTOS:
{string.Join("\n\n---\n\n", selectedChunks.Select(c => $"[Documento: {c.Source}]\n{c.Content}"))}

REGRAS IMPORTANTES:
1. Responda APENAS com informações que estão nos documentos acima
2. Se a informação não estiver nos documentos, diga claramente: 'Esta informação não foi encontrada nos documentos carregados.'
3. Cite o nome do documento entre colchetes [nome.txt] ao mencionar informações
4. Seja objetivo e preciso
5. NÃO invente informações que não estão nos documentos

FORMATO DA RESPOSTA:
- Comece com: '📄 **Resposta baseada nos documentos:**'
- Ao final, adicione: '📚 **Fontes:** [lista dos documentos usados]'
";

        var answer = await llmService.GenerateResponseAsync(answerPrompt);

        // Garantir que a resposta tenha indicação de fontes
        if (!answer.Contains("Fontes:") && !answer.Contains("📚"))
        {
            answer += $"\n\n📚 **Fontes consultadas:** {string.Join(", ", usedSources)}";
        }

        return Results.Ok(new
        {
            success = true,
            answer,
            sources = usedSources,
            chunksUsed = selectedChunks.Count,
            totalChunksAvailable = chunks.Count
        });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { success = false, error = ex.Message });
    }
});

app.Logger.LogInformation("🚀 Sistema de Agentes Autônomos rodando em http://localhost:5130");
app.Logger.LogInformation("📡 SignalR Hub disponível em /hubs/agent");
app.Logger.LogInformation("🤖 Endpoints: /run-task (legado), /run-autonomous (novo)");
app.Logger.LogInformation("📚 RAG Endpoints: /rag/upload, /rag/upload-text, /rag/query, /rag/stats");

app.Run();

// ============================================
// DTOs
// ============================================
public record RagTextUploadRequest(string Content, string? FileName = null, string? SessionId = null);
public record RagQueryRequest(string Query, string? SessionId = null);