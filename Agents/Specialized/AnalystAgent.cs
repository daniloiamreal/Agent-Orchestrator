using Agent.Orchestrator.Api.Agents.Base;
using Agent.Orchestrator.Api.DTOs;
using Agent.Orchestrator.Api.Services;
using System.Text;
using System.Text.Json;

namespace Agent.Orchestrator.Api.Agents.Specialized;

/// <summary>
/// Agente analista - análise de dados, interpretação, validações e decisões lógicas
/// </summary>
public class AnalystAgent : BaseAgent
{
    public override string Name => "AnalystAgent";
    public override string Description => "Análise de dados, validações, interpretações e tomada de decisões lógicas";
    public override IReadOnlyList<string> Capabilities => new[]
    {
        "analyze-data",
        "make-decision",
        "validate-logic",
        "compare-options",
        "evaluate-results",
        "generate-report",
        "statistical-analysis",
        "sentiment-analysis"
    };

    public AnalystAgent(
        ILLMService llmService,
        IWorkspaceService workspaceService,
        IAgentEventBus eventBus,
        ISharedMemory memory,
        ILogger<AnalystAgent> logger)
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
                "analyze-data" => await AnalyzeDataAsync(step, context, cancellationToken),
                "make-decision" => await MakeDecisionAsync(step, context, cancellationToken),
                "validate-logic" => await ValidateLogicAsync(step, context, cancellationToken),
                "compare-options" => await CompareOptionsAsync(step, context, cancellationToken),
                "evaluate-results" => await EvaluateResultsAsync(step, context, cancellationToken),
                "generate-report" => await GenerateReportAsync(step, context, cancellationToken),
                "statistical-analysis" => await StatisticalAnalysisAsync(step, context, cancellationToken),
                "sentiment-analysis" => await SentimentAnalysisAsync(step, context, cancellationToken),
                _ => await PerformAnalysisAsync(step, context, cancellationToken)
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
    /// Analisa dados fornecidos
    /// </summary>
    private async Task<string> AnalyzeDataAsync(TaskStep step, TaskExecutionContext context, CancellationToken cancellationToken)
    {
        var data = step.Parameters.TryGetValue("data", out var d) ? d.ToString() : null;
        
        // Coletar dados de outros agentes
        var sharedData = context.SharedState
            .Where(kv => kv.Value != null)
            .Select(kv => $"**{kv.Key}:** {TruncateString(kv.Value?.ToString(), 500)}")
            .ToList();

        var previousResults = context.AgentResults
            .Select(r => $"**[{r.AgentName}]:** {TruncateString(r.Result?.ToString(), 300)}")
            .ToList();

        var prompt = $@"
Você é um analista de dados especializado.

TAREFA: Analisar dados e fornecer insights

DADOS FORNECIDOS:
{data ?? "Nenhum dado específico fornecido"}

DADOS COMPARTILHADOS DO WORKFLOW:
{(sharedData.Any() ? string.Join("\n", sharedData) : "Nenhum")}

RESULTADOS DE OUTROS AGENTES:
{(previousResults.Any() ? string.Join("\n", previousResults) : "Nenhum")}

CONTEXTO: {context.Prompt}

Forneça uma análise completa incluindo:
1. **Resumo Executivo** - Principais descobertas em 2-3 frases
2. **Análise Detalhada** - Exploração dos dados
3. **Padrões Identificados** - Tendências e correlações
4. **Métricas Chave** - Números importantes
5. **Insights Acionáveis** - O que fazer com essas informações
6. **Recomendações** - Próximos passos sugeridos
";

        var result = await CallLLMWithRetryAsync(prompt, cancellationToken: cancellationToken);
        
        await SaveToMemoryAsync($"analysis_{context.TaskId}", result);
        context.SharedState["AnalysisResult"] = result;
        
        return result;
    }

    /// <summary>
    /// Toma decisão baseada em critérios
    /// </summary>
    private async Task<string> MakeDecisionAsync(TaskStep step, TaskExecutionContext context, CancellationToken cancellationToken)
    {
        var options = step.Parameters.TryGetValue("options", out var o) ? o.ToString() : null;
        var criteria = step.Parameters.TryGetValue("criteria", out var c) ? c.ToString() : null;

        var prompt = $@"
Você é um especialista em tomada de decisões estruturadas.

DECISÃO A TOMAR: {context.Prompt}

OPÇÕES DISPONÍVEIS:
{options ?? "Não especificadas - analise o contexto e identifique opções"}

CRITÉRIOS DE AVALIAÇÃO:
{criteria ?? "Use critérios padrão: custo, tempo, risco, qualidade, viabilidade"}

CONTEXTO ADICIONAL:
{string.Join("\n", context.SharedState.Select(kv => $"- {kv.Key}: {TruncateString(kv.Value?.ToString(), 200)}"))}

Forneça:
1. **Análise de cada opção** - Prós e contras
2. **Matriz de decisão** - Pontuação por critério
3. **Recomendação** - Opção escolhida e justificativa
4. **Riscos** - O que pode dar errado
5. **Plano de contingência** - Se a decisão falhar
6. **Próximos passos** - Ações imediatas

Use uma escala de 1-10 para pontuar cada critério.
";

        var decision = await CallLLMWithRetryAsync(prompt, cancellationToken: cancellationToken);
        
        context.SharedState["Decision"] = decision;
        
        return decision;
    }

    /// <summary>
    /// Valida lógica ou código
    /// </summary>
    private async Task<string> ValidateLogicAsync(TaskStep step, TaskExecutionContext context, CancellationToken cancellationToken)
    {
        var code = context.SharedState.TryGetValue("GeneratedCode", out var gc) ? gc?.ToString() : 
                   context.GeneratedCode;

        var prompt = $@"
Você é um especialista em validação de lógica e qualidade de código.

CONTEÚDO A VALIDAR:
{code ?? context.Prompt}

Realize uma validação completa:

1. **Análise de Corretude**
   - A lógica está correta?
   - Há erros lógicos?
   - Edge cases tratados?

2. **Qualidade do Código**
   - Legibilidade
   - Manutenibilidade
   - Aderência a padrões

3. **Segurança**
   - Vulnerabilidades potenciais
   - Validação de entrada
   - Tratamento de erros

4. **Performance**
   - Complexidade algorítmica
   - Uso de recursos
   - Otimizações sugeridas

5. **Veredito Final**
   - ? APROVADO
   - ?? APROVADO COM RESSALVAS
   - ? REPROVADO

Forneça recomendações específicas de melhoria.
";

        var validation = await CallLLMWithRetryAsync(prompt, cancellationToken: cancellationToken);
        
        context.SharedState["ValidationResult"] = validation;
        
        return validation;
    }

    /// <summary>
    /// Compara opções
    /// </summary>
    private async Task<string> CompareOptionsAsync(TaskStep step, TaskExecutionContext context, CancellationToken cancellationToken)
    {
        var prompt = $@"
Você é um analista comparativo especializado.

COMPARAÇÃO SOLICITADA: {context.Prompt}

PARÂMETROS: {JsonSerializer.Serialize(step.Parameters)}

DADOS DISPONÍVEIS:
{string.Join("\n", context.SharedState.Select(kv => $"- {kv.Key}: {TruncateString(kv.Value?.ToString(), 200)}"))}

Forneça uma comparação detalhada:

1. **Tabela Comparativa** - Características lado a lado
2. **Pontos Fortes** - De cada opção
3. **Pontos Fracos** - De cada opção
4. **Custo-Benefício** - Análise de valor
5. **Casos de Uso** - Quando usar cada um
6. **Recomendação** - Melhor escolha e contexto
";

        var comparison = await CallLLMWithRetryAsync(prompt, cancellationToken: cancellationToken);
        
        context.SharedState["Comparison"] = comparison;
        
        return comparison;
    }

    /// <summary>
    /// Avalia resultados de execução
    /// </summary>
    private async Task<string> EvaluateResultsAsync(TaskStep step, TaskExecutionContext context, CancellationToken cancellationToken)
    {
        var results = context.AgentResults
            .Select(r => new { r.AgentName, r.Success, Result = TruncateString(r.Result?.ToString(), 500), r.Error })
            .ToList();

        var prompt = $@"
Você é um avaliador de qualidade de resultados.

OBJETIVO ORIGINAL: {context.Prompt}

RESULTADOS DOS AGENTES:
{JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true })}

PLANO EXECUTADO:
{(context.Plan != null ? string.Join("\n", context.Plan.Steps.Select(s => $"- {s.Order}. {s.AgentName}: {s.Action} [{s.Status}]")) : "N/A")}

Avalie:
1. **Taxa de Sucesso** - Quantos agentes concluíram com sucesso
2. **Qualidade dos Resultados** - Nota de 1 a 10
3. **Objetivo Atingido?** - O objetivo original foi cumprido?
4. **Gaps Identificados** - O que ficou faltando
5. **Melhorias Sugeridas** - Para próximas execuções
6. **Resumo Executivo** - Conclusão em 3 frases
";

        var evaluation = await CallLLMWithRetryAsync(prompt, cancellationToken: cancellationToken);
        
        context.SharedState["Evaluation"] = evaluation;
        
        return evaluation;
    }

    /// <summary>
    /// Gera relatório completo
    /// </summary>
    private async Task<string> GenerateReportAsync(TaskStep step, TaskExecutionContext context, CancellationToken cancellationToken)
    {
        var reportType = step.Parameters.TryGetValue("type", out var t) ? t.ToString() : "executivo";

        var prompt = $@"
Você é um especialista em geração de relatórios profissionais.

TIPO DE RELATÓRIO: {reportType}
CONTEXTO: {context.Prompt}

DADOS PARA O RELATÓRIO:
{string.Join("\n\n", context.SharedState.Select(kv => $"### {kv.Key}\n{kv.Value}"))}

RESULTADOS DOS AGENTES:
{string.Join("\n", context.AgentResults.Select(r => $"- {r.AgentName}: {TruncateString(r.Result?.ToString(), 300)}"))}

Gere um relatório profissional em Markdown incluindo:
1. **Capa** - Título, data, autor
2. **Sumário Executivo** - Principais pontos em 1 parágrafo
3. **Introdução** - Contexto e objetivos
4. **Metodologia** - Como foi feito
5. **Resultados** - Descobertas detalhadas
6. **Análise** - Interpretação dos resultados
7. **Conclusões** - Principais takeaways
8. **Recomendações** - Próximos passos
9. **Apêndices** - Dados de suporte

Use formatação Markdown profissional.
";

        var report = await CallLLMWithRetryAsync(prompt, cancellationToken: cancellationToken);
        
        var fileName = $"report_{DateTime.Now:yyyyMMdd_HHmmss}.md";
        await _workspaceService.SaveFileAsync(fileName, report);
        LogStep(context, $"?? Relatório salvo: {fileName}");
        
        context.SharedState["Report"] = report;
        
        return report;
    }

    /// <summary>
    /// Análise estatística
    /// </summary>
    private async Task<string> StatisticalAnalysisAsync(TaskStep step, TaskExecutionContext context, CancellationToken cancellationToken)
    {
        var data = step.Parameters.TryGetValue("data", out var d) ? d.ToString() : context.Prompt;

        var prompt = $@"
Você é um estatístico especializado em análise de dados.

DADOS PARA ANÁLISE:
{data}

Forneça análise estatística completa:

1. **Estatísticas Descritivas**
   - Média, mediana, moda
   - Desvio padrão, variância
   - Mínimo, máximo, amplitude

2. **Distribuição**
   - Tipo de distribuição
   - Assimetria e curtose
   - Outliers identificados

3. **Visualizações Sugeridas**
   - Gráficos recomendados
   - Código para gerar (Python/R)

4. **Testes Estatísticos**
   - Testes apropriados
   - Hipóteses
   - Resultados esperados

5. **Interpretação**
   - Significância
   - Conclusões
   - Limitações
";

        var analysis = await CallLLMWithRetryAsync(prompt, cancellationToken: cancellationToken);
        
        context.SharedState["StatisticalAnalysis"] = analysis;
        
        return analysis;
    }

    /// <summary>
    /// Análise de sentimento
    /// </summary>
    private async Task<string> SentimentAnalysisAsync(TaskStep step, TaskExecutionContext context, CancellationToken cancellationToken)
    {
        var text = step.Parameters.TryGetValue("text", out var t) ? t.ToString() : context.Prompt;

        var prompt = $@"
Você é um especialista em análise de sentimento e processamento de linguagem natural.

TEXTO PARA ANÁLISE:
{text}

Forneça:

1. **Sentimento Geral**
   - Classificação: Positivo / Neutro / Negativo
   - Score: -1.0 a +1.0
   - Confiança: Porcentagem

2. **Emoções Detectadas**
   - Alegria, tristeza, raiva, medo, surpresa, etc.
   - Intensidade de cada emoção

3. **Análise de Tom**
   - Formal/Informal
   - Objetivo/Subjetivo
   - Urgência

4. **Entidades e Tópicos**
   - Pessoas, lugares, organizações mencionadas
   - Temas principais

5. **Insights**
   - Intenção do autor
   - Público-alvo provável
   - Recomendações de ação
";

        var sentiment = await CallLLMWithRetryAsync(prompt, cancellationToken: cancellationToken);
        
        context.SharedState["SentimentAnalysis"] = sentiment;
        
        return sentiment;
    }

    /// <summary>
    /// Análise genérica
    /// </summary>
    private async Task<string> PerformAnalysisAsync(TaskStep step, TaskExecutionContext context, CancellationToken cancellationToken)
    {
        var sharedData = context.SharedState
            .Select(kv => $"- {kv.Key}: {TruncateString(kv.Value?.ToString(), 300)}")
            .ToList();

        var prompt = $@"
Você é um analista sênior multidisciplinar.

TAREFA: {step.Action}
CONTEXTO: {context.Prompt}
PARÂMETROS: {JsonSerializer.Serialize(step.Parameters)}

DADOS DISPONÍVEIS:
{string.Join("\n", sharedData)}

RESULTADOS ANTERIORES:
{string.Join("\n", context.AgentResults.Select(r => $"[{r.AgentName}]: {TruncateString(r.Result?.ToString(), 300)}"))}

Forneça uma análise completa e acionável.
";

        var result = await CallLLMWithRetryAsync(prompt, cancellationToken: cancellationToken);
        
        await SaveToMemoryAsync($"analysis_result_{context.TaskId}", result);
        context.SharedState["AnalysisResult"] = result;
        
        return result;
    }

    private string TruncateString(string? str, int maxLength)
    {
        if (string.IsNullOrEmpty(str)) return "";
        return str.Length <= maxLength ? str : str.Substring(0, maxLength) + "...";
    }
}
