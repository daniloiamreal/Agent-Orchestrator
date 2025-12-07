using Agent.Orchestrator.Api.Agents.Base;
using Agent.Orchestrator.Api.DTOs;
using Agent.Orchestrator.Api.Services;
using System.Text;
using System.Text.RegularExpressions;

namespace Agent.Orchestrator.Api.Agents.Specialized;

/// <summary>
/// Agente de Retrieval-Augmented Generation (RAG)
/// Busca em documentos e bases de conhecimento
/// </summary>
public class RAGAgent : BaseAgent
{
    private static readonly Dictionary<string, List<DocumentChunk>> _documentStore = new();
    private static readonly object _lock = new();

    public override string Name => "RAGAgent";
    public override string Description => "Busca em documentos e bases de conhecimento usando RAG";
    public override IReadOnlyList<string> Capabilities => new[]
    {
        "search-documents",
        "retrieve-context",
        "semantic-search",
        "embed-text",
        "chunk-document",
        "upload-document"
    };

    public RAGAgent(
        ILLMService llmService,
        IWorkspaceService workspaceService,
        IAgentEventBus eventBus,
        ISharedMemory memory,
        ILogger<RAGAgent> logger)
        : base(llmService, workspaceService, eventBus, memory, logger)
    {
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
                "upload-document" => await ProcessUploadAsync(step, context, cancellationToken),
                "chunk-document" => await ChunkDocumentAsync(step, context, cancellationToken),
                "search-documents" or "semantic-search" => await SemanticSearchAsync(step, context, cancellationToken),
                "retrieve-context" => await RetrieveContextAsync(step, context, cancellationToken),
                _ => await PerformRAGQueryAsync(step, context, cancellationToken)
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
                StackTrace = ex.StackTrace,
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
    /// Processa upload de documento e divide em chunks
    /// </summary>
    public async Task<string> ProcessUploadAsync(TaskStep step, TaskExecutionContext context, CancellationToken cancellationToken)
    {
        var content = step.Parameters.TryGetValue("content", out var c) ? c.ToString() : "";
        var fileName = step.Parameters.TryGetValue("fileName", out var f) ? f.ToString() : "documento.txt";

        if (string.IsNullOrWhiteSpace(content))
        {
            return "Nenhum conteúdo fornecido para upload.";
        }

        LogStep(context, $"?? Processando documento: {fileName}");

        // Chunkar o documento
        var chunks = ChunkText(content!, fileName!);
        
        lock (_lock)
        {
            var key = $"global_{context.TaskId}";
            if (!_documentStore.ContainsKey(key))
            {
                _documentStore[key] = new List<DocumentChunk>();
            }
            _documentStore[key].AddRange(chunks);
        }

        LogStep(context, $"? Documento processado: {chunks.Count} chunks criados");

        // Gerar resumo do documento usando LLM
        var summaryPrompt = $@"
Analise o seguinte documento e forneça um resumo conciso:

DOCUMENTO: {fileName}
CONTEÚDO (primeiros 3000 caracteres):
{content!.Substring(0, Math.Min(content.Length, 3000))}

Forneça:
1. Resumo executivo (2-3 frases)
2. Tópicos principais (lista)
3. Palavras-chave relevantes
";

        var summary = await CallLLMWithRetryAsync(summaryPrompt, cancellationToken: cancellationToken);
        
        await SaveToMemoryAsync($"document_{fileName}", new { FileName = fileName, ChunkCount = chunks.Count, Summary = summary });
        
        return $"?? Documento '{fileName}' carregado com sucesso!\n?? {chunks.Count} chunks criados\n\n{summary}";
    }

    /// <summary>
    /// Divide texto em chunks menores
    /// </summary>
    private List<DocumentChunk> ChunkText(string text, string source, int chunkSize = 500, int overlap = 50)
    {
        var chunks = new List<DocumentChunk>();
        var sentences = Regex.Split(text, @"(?<=[.!?])\s+");
        var currentChunk = new StringBuilder();
        var chunkIndex = 0;

        foreach (var sentence in sentences)
        {
            if (currentChunk.Length + sentence.Length > chunkSize && currentChunk.Length > 0)
            {
                chunks.Add(new DocumentChunk
                {
                    Id = Guid.NewGuid().ToString(),
                    Content = currentChunk.ToString().Trim(),
                    Source = source,
                    Index = chunkIndex++,
                    CreatedAt = DateTime.UtcNow
                });

                // Overlap: manter últimas palavras para contexto
                var words = currentChunk.ToString().Split(' ');
                currentChunk.Clear();
                if (words.Length > 10)
                {
                    currentChunk.Append(string.Join(" ", words.Skip(words.Length - 10)));
                    currentChunk.Append(" ");
                }
            }
            currentChunk.Append(sentence).Append(" ");
        }

        // Adicionar último chunk
        if (currentChunk.Length > 0)
        {
            chunks.Add(new DocumentChunk
            {
                Id = Guid.NewGuid().ToString(),
                Content = currentChunk.ToString().Trim(),
                Source = source,
                Index = chunkIndex,
                CreatedAt = DateTime.UtcNow
            });
        }

        return chunks;
    }

    /// <summary>
    /// Chunka documento explicitamente
    /// </summary>
    private async Task<string> ChunkDocumentAsync(TaskStep step, TaskExecutionContext context, CancellationToken cancellationToken)
    {
        var content = step.Parameters.TryGetValue("content", out var c) ? c.ToString() : "";
        var source = step.Parameters.TryGetValue("source", out var s) ? s.ToString() : "documento";

        var chunks = ChunkText(content ?? "", source ?? "documento");
        
        lock (_lock)
        {
            var key = $"global_{context.TaskId}";
            if (!_documentStore.ContainsKey(key))
            {
                _documentStore[key] = new List<DocumentChunk>();
            }
            _documentStore[key].AddRange(chunks);
        }

        return $"Documento dividido em {chunks.Count} chunks.";
    }

    /// <summary>
    /// Busca semântica nos documentos carregados
    /// </summary>
    private async Task<string> SemanticSearchAsync(TaskStep step, TaskExecutionContext context, CancellationToken cancellationToken)
    {
        var query = step.Parameters.TryGetValue("query", out var q) ? q.ToString() : context.Prompt;
        
        LogStep(context, $"?? Buscando nos documentos: {query}");

        // Obter chunks disponíveis
        List<DocumentChunk> allChunks;
        lock (_lock)
        {
            allChunks = _documentStore
                .Where(kv => kv.Key.StartsWith("global_"))
                .SelectMany(kv => kv.Value)
                .ToList();
        }

        if (!allChunks.Any())
        {
            LogStep(context, "?? NENHUM DOCUMENTO CARREGADO - Usando conhecimento geral");
            
            var fallbackPrompt = $@"
Você é um assistente de busca RAG.

CONSULTA DO USUÁRIO: {query}

?? ATENÇÃO: Não há documentos carregados no sistema.

Forneça uma resposta baseada no seu conhecimento geral, mas deixe claro que:
1. Esta resposta NÃO vem de documentos do usuário
2. O usuário deve fazer upload de documentos para respostas específicas
3. Sugira que tipo de documento seria útil para esta consulta

Comece sua resposta com: '?? **Resposta do conhecimento geral (sem documentos):**'
";
            var fallbackAnswer = await CallLLMWithRetryAsync(fallbackPrompt, cancellationToken: cancellationToken);
            context.SharedState["RAGSource"] = "knowledge_base";
            return fallbackAnswer;
        }

        // Temos documentos! Logar isso
        var sources = allChunks.Select(c => c.Source).Distinct().ToList();
        LogStep(context, $"?? Encontrados {allChunks.Count} chunks de {sources.Count} documento(s): {string.Join(", ", sources)}");

        // Usar LLM para ranquear chunks por relevância
        var chunkPreviews = allChunks
            .Select((c, i) => $"[{i}] ({c.Source}): {c.Content.Substring(0, Math.Min(c.Content.Length, 200))}")
            .ToList();

        var rankingPrompt = $@"
Você é um sistema de ranking de relevância para RAG.

CONSULTA: {query}

CHUNKS DISPONÍVEIS:
{string.Join("\n---\n", chunkPreviews.Take(20))}

Retorne APENAS os índices (números) dos 3 chunks MAIS RELEVANTES para a consulta.
Formato: número, número, número
Exemplo: 0, 5, 12
";

        var rankingResult = await CallLLMWithRetryAsync(rankingPrompt, cancellationToken: cancellationToken);
        
        // Extrair índices da resposta
        var indices = Regex.Matches(rankingResult, @"\d+")
            .Select(m => int.TryParse(m.Value, out var i) ? i : -1)
            .Where(i => i >= 0 && i < allChunks.Count)
            .Distinct()
            .Take(3)
            .ToList();

        var selectedChunks = indices.Any() 
            ? indices.Select(i => allChunks[i]).ToList()
            : allChunks.Take(3).ToList();

        var usedSources = selectedChunks.Select(c => c.Source).Distinct().ToList();
        LogStep(context, $"? Usando {selectedChunks.Count} chunks relevantes de: {string.Join(", ", usedSources)}");

        // Gerar resposta usando os chunks selecionados
        var contextPrompt = $@"
Você é um assistente RAG que responde perguntas baseado em documentos DO USUÁRIO.

PERGUNTA DO USUÁRIO: {query}

?? CONTEXTO DOS DOCUMENTOS CARREGADOS:
{string.Join("\n\n---\n\n", selectedChunks.Select(c => $"[Fonte: {c.Source}]\n{c.Content}"))}

INSTRUÇÕES IMPORTANTES:
1. Responda APENAS usando informações do contexto fornecido acima
2. SEMPRE cite a fonte do documento entre colchetes [Nome do arquivo]
3. Se a informação não estiver no contexto, diga: 'Esta informação não foi encontrada nos documentos carregados.'
4. Seja preciso e objetivo

FORMATO DA RESPOSTA:
Comece com: '?? **Resposta baseada nos documentos:**'
Ao final, liste: '?? **Fontes consultadas:** [lista de arquivos]'
";

        var answer = await CallLLMWithRetryAsync(contextPrompt, cancellationToken: cancellationToken);
        
        // Adicionar indicador de fontes se não estiver presente
        if (!answer.Contains("Fontes consultadas"))
        {
            answer += $"\n\n?? **Fontes consultadas:** {string.Join(", ", usedSources)}";
        }

        // Salvar contexto para outros agentes
        context.SharedState["RAGContext"] = string.Join("\n\n", selectedChunks.Select(c => c.Content));
        context.SharedState["RAGSources"] = string.Join(", ", usedSources);
        context.SharedState["RAGSource"] = "documents";
        context.SharedState["RAGChunksUsed"] = selectedChunks.Count;

        return answer;
    }

    /// <summary>
    /// Recupera contexto para uso por outros agentes
    /// </summary>
    private async Task<string> RetrieveContextAsync(TaskStep step, TaskExecutionContext context, CancellationToken cancellationToken)
    {
        return await SemanticSearchAsync(step, context, cancellationToken);
    }

    /// <summary>
    /// Executa query RAG completa
    /// </summary>
    private async Task<string> PerformRAGQueryAsync(TaskStep step, TaskExecutionContext context, CancellationToken cancellationToken)
    {
        var query = step.Parameters.TryGetValue("query", out var q) ? q.ToString() : context.Prompt;
        
        LogStep(context, $"?? Executando RAG Query: {query}");

        return await SemanticSearchAsync(step, context, cancellationToken);
    }

    /// <summary>
    /// Adiciona documento ao store (chamado externamente)
    /// </summary>
    public static void AddDocument(string sessionId, string content, string fileName)
    {
        var chunks = new List<DocumentChunk>();
        var sentences = Regex.Split(content, @"(?<=[.!?])\s+");
        var currentChunk = new StringBuilder();
        var chunkIndex = 0;

        foreach (var sentence in sentences)
        {
            if (currentChunk.Length + sentence.Length > 500 && currentChunk.Length > 0)
            {
                chunks.Add(new DocumentChunk
                {
                    Id = Guid.NewGuid().ToString(),
                    Content = currentChunk.ToString().Trim(),
                    Source = fileName,
                    Index = chunkIndex++,
                    CreatedAt = DateTime.UtcNow
                });
                currentChunk.Clear();
            }
            currentChunk.Append(sentence).Append(" ");
        }

        if (currentChunk.Length > 0)
        {
            chunks.Add(new DocumentChunk
            {
                Id = Guid.NewGuid().ToString(),
                Content = currentChunk.ToString().Trim(),
                Source = fileName,
                Index = chunkIndex,
                CreatedAt = DateTime.UtcNow
            });
        }

        lock (_lock)
        {
            var key = $"global_{sessionId}";
            if (!_documentStore.ContainsKey(key))
            {
                _documentStore[key] = new List<DocumentChunk>();
            }
            _documentStore[key].AddRange(chunks);
        }
    }

    /// <summary>
    /// Obtém estatísticas dos documentos
    /// </summary>
    public static DocumentStats GetStats(string sessionId)
    {
        lock (_lock)
        {
            var chunks = _documentStore
                .Where(kv => kv.Key.StartsWith("global_"))
                .SelectMany(kv => kv.Value)
                .ToList();

            return new DocumentStats
            {
                TotalChunks = chunks.Count,
                TotalCharacters = chunks.Sum(x => x.Content.Length),
                Sources = chunks.Select(x => x.Source).Distinct().ToList()
            };
        }
    }

    /// <summary>
    /// Obtém todos os chunks de documentos (para consulta externa)
    /// </summary>
    public static List<DocumentChunk> GetAllChunks(string sessionId)
    {
        lock (_lock)
        {
            return _documentStore
                .Where(kv => kv.Key.StartsWith("global_"))
                .SelectMany(kv => kv.Value)
                .ToList();
        }
    }

    /// <summary>
    /// Limpa documentos
    /// </summary>
    public static void ClearDocuments(string sessionId)
    {
        lock (_lock)
        {
            var keysToRemove = _documentStore.Keys.Where(k => k.StartsWith($"global_{sessionId}")).ToList();
            foreach (var key in keysToRemove)
            {
                _documentStore.Remove(key);
            }
        }
    }
}

/// <summary>
/// Representa um chunk de documento
/// </summary>
public class DocumentChunk
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Content { get; set; } = "";
    public string Source { get; set; } = "";
    public int Index { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Dictionary<string, object> Metadata { get; set; } = new();
}

/// <summary>
/// Estatísticas de documentos
/// </summary>
public class DocumentStats
{
    public int TotalChunks { get; set; }
    public int TotalCharacters { get; set; }
    public List<string> Sources { get; set; } = new();
}
