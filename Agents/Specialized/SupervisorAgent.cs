using Agent.Orchestrator.Api.Agents.Base;
using Agent.Orchestrator.Api.DTOs;
using Agent.Orchestrator.Api.Services;
using System.Text;
using System.Text.Json;

namespace Agent.Orchestrator.Api.Agents.Specialized;

/// <summary>
/// Agente supervisor - coordena múltiplos agentes, valida resultados e gerencia workflows
/// </summary>
public class SupervisorAgent : BaseAgent
{
    public override string Name => "SupervisorAgent";
    public override string Description => "Coordena múltiplos agentes, valida resultados e gerencia workflows complexos";
    public override IReadOnlyList<string> Capabilities => new[]
    {
        "coordinate-agents",
        "validate-results",
        "manage-workflow",
        "approve-actions",
        "summarize-work",
        "quality-control",
        "conflict-resolution",
        "prioritize-tasks"
    };

    public SupervisorAgent(
        ILLMService llmService,
        IWorkspaceService workspaceService,
        IAgentEventBus eventBus,
        ISharedMemory memory,
        ILogger<SupervisorAgent> logger)
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
                "validate-results" => await ValidateResultsAsync(context, cancellationToken),
                "summarize-work" => await SummarizeWorkAsync(context, cancellationToken),
                "quality-control" => await QualityControlAsync(context, cancellationToken),
                "conflict-resolution" => await ResolveConflictsAsync(step, context, cancellationToken),
                "prioritize-tasks" => await PrioritizeTasksAsync(step, context, cancellationToken),
                "approve-actions" => await ApproveActionsAsync(step, context, cancellationToken),
                "coordinate-agents" or "manage-workflow" => await CoordinateAsync(step, context, cancellationToken),
                _ => await SuperviseAsync(step, context, cancellationToken)
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
    /// Valida resultados de todos os agentes
    /// </summary>
    private async Task<string> ValidateResultsAsync(TaskExecutionContext context, CancellationToken cancellationToken)
    {
        var results = context.AgentResults
            .Select(r => new 
            { 
                r.AgentName, 
                r.Success, 
                ResultPreview = TruncateString(r.Result?.ToString(), 500),
                r.Error,
                r.Timestamp
            })
            .ToList();

        var prompt = $@"
Você é um supervisor de qualidade que valida o trabalho de uma equipe de agentes de IA.

OBJETIVO ORIGINAL DO USUÁRIO:
{context.Prompt}

PLANO EXECUTADO:
{(context.Plan != null ? string.Join("\n", context.Plan.Steps.Select(s => $"  {s.Order}. {s.AgentName}: {s.Action} ? {s.Status}")) : "Plano não disponível")}

RESULTADOS DOS AGENTES:
{JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true })}

DADOS COMPARTILHADOS:
{string.Join("\n", context.SharedState.Take(5).Select(kv => $"- {kv.Key}: {TruncateString(kv.Value?.ToString(), 200)}"))}

Realize uma validação completa:

## 1. Checklist de Validação
| Critério | Status | Observação |
|----------|--------|------------|
| Objetivo atingido? | ?/? | ... |
| Qualidade aceitável? | ?/? | ... |
| Erros encontrados? | ?/? | ... |
| Dados consistentes? | ?/? | ... |

## 2. Avaliação por Agente
Para cada agente, avalie de 1 a 10.

## 3. Problemas Identificados
Liste problemas encontrados.

## 4. Recomendações
O que precisa ser corrigido ou melhorado.

## 5. Veredito Final
**APROVADO** ? | **REPROVADO** ? | **REQUER REVISÃO** ??

Justifique sua decisão.
";

        var validation = await CallLLMWithRetryAsync(prompt, cancellationToken: cancellationToken);
        
        context.SharedState["SupervisorValidation"] = validation;
        
        // Determinar se foi aprovado
        var approved = validation.Contains("APROVADO") && !validation.Contains("REPROVADO");
        context.SharedState["ValidationApproved"] = approved;

        return validation;
    }

    /// <summary>
    /// Resume todo o trabalho realizado
    /// </summary>
    private async Task<string> SummarizeWorkAsync(TaskExecutionContext context, CancellationToken cancellationToken)
    {
        var duration = context.CompletedAt.HasValue 
            ? (context.CompletedAt.Value - context.StartedAt).TotalSeconds 
            : (DateTime.UtcNow - context.StartedAt).TotalSeconds;

        var prompt = $@"
Você é um supervisor que cria resumos executivos de trabalhos complexos.

SOLICITAÇÃO ORIGINAL:
{context.Prompt}

TEMPO DE EXECUÇÃO: {duration:F1} segundos

PLANO EXECUTADO:
{(context.Plan != null ? string.Join("\n", context.Plan.Steps.Select(s => $"- [{s.Status}] {s.AgentName}: {s.Action}")) : "N/A")}

AGENTES QUE PARTICIPARAM:
{string.Join(", ", context.AgentResults.Select(r => r.AgentName).Distinct())}

RESULTADOS PRINCIPAIS:
{string.Join("\n\n", context.AgentResults.Select(r => $"### {r.AgentName}\n{TruncateString(r.Result?.ToString(), 400)}"))}

ERROS ENCONTRADOS:
{string.Join("\n", context.Errors)}

Crie um resumo executivo profissional:

## ?? Resumo Executivo

### O que foi solicitado
(1-2 frases)

### O que foi feito
(lista de ações principais)

### Resultados Obtidos
(principais entregas)

### Métricas
- Tempo total: X segundos
- Agentes utilizados: X
- Taxa de sucesso: X%

### Arquivos Gerados
(se houver)

### Próximos Passos Recomendados
(sugestões)

### Conclusão
(1 parágrafo final)
";

        var summary = await CallLLMWithRetryAsync(prompt, cancellationToken: cancellationToken);
        
        // Salvar resumo
        var fileName = $"summary_{context.TaskId.Substring(0, 8)}_{DateTime.Now:yyyyMMdd_HHmmss}.md";
        await _workspaceService.SaveFileAsync(fileName, summary);
        LogStep(context, $"?? Resumo salvo: {fileName}");
        
        context.SharedState["ExecutiveSummary"] = summary;
        
        return summary;
    }

    /// <summary>
    /// Controle de qualidade
    /// </summary>
    private async Task<string> QualityControlAsync(TaskExecutionContext context, CancellationToken cancellationToken)
    {
        var prompt = $@"
Você é um especialista em controle de qualidade (QA).

TRABALHO REALIZADO:
{string.Join("\n", context.AgentResults.Select(r => $"[{r.AgentName}]: {TruncateString(r.Result?.ToString(), 400)}"))}

Realize uma auditoria de qualidade:

## 1. Métricas de Qualidade

| Métrica | Score (1-10) | Comentário |
|---------|--------------|------------|
| Completude | | |
| Precisão | | |
| Clareza | | |
| Usabilidade | | |
| Consistência | | |

## 2. Problemas de Qualidade
- Críticos (bloqueadores)
- Importantes (devem ser corrigidos)
- Menores (nice to have)

## 3. Conformidade
- Segue boas práticas?
- Atende requisitos?
- Documentação adequada?

## 4. Score Final: X/10

## 5. Ações Corretivas
O que precisa ser feito para melhorar.
";

        var qc = await CallLLMWithRetryAsync(prompt, cancellationToken: cancellationToken);
        
        context.SharedState["QualityControl"] = qc;
        
        return qc;
    }

    /// <summary>
    /// Resolve conflitos entre resultados de agentes
    /// </summary>
    private async Task<string> ResolveConflictsAsync(TaskStep step, TaskExecutionContext context, CancellationToken cancellationToken)
    {
        var prompt = $@"
Você é um mediador que resolve conflitos entre diferentes fontes de informação.

CONTEXTO: {context.Prompt}

RESULTADOS POTENCIALMENTE CONFLITANTES:
{string.Join("\n\n", context.AgentResults.Select(r => $"### {r.AgentName}\n{r.Result}"))}

Analise os resultados e:
1. Identifique contradições ou inconsistências
2. Determine a versão mais confiável
3. Proponha uma síntese que resolva os conflitos
4. Explique o raciocínio

Forneça uma resolução clara e fundamentada.
";

        var resolution = await CallLLMWithRetryAsync(prompt, cancellationToken: cancellationToken);
        
        context.SharedState["ConflictResolution"] = resolution;
        
        return resolution;
    }

    /// <summary>
    /// Prioriza tarefas
    /// </summary>
    private async Task<string> PrioritizeTasksAsync(TaskStep step, TaskExecutionContext context, CancellationToken cancellationToken)
    {
        var tasks = step.Parameters.TryGetValue("tasks", out var t) ? t.ToString() : context.Prompt;

        var prompt = $@"
Você é um gerente de projeto especializado em priorização.

TAREFAS A PRIORIZAR:
{tasks}

CONTEXTO:
{string.Join("\n", context.SharedState.Take(3).Select(kv => $"- {kv.Key}: {TruncateString(kv.Value?.ToString(), 100)}"))}

Priorize usando a matriz de Eisenhower (Urgente/Importante):

## Matriz de Priorização

### ?? Urgente + Importante (FAZER AGORA)
1. ...

### ?? Importante + Não Urgente (AGENDAR)
1. ...

### ?? Urgente + Não Importante (DELEGAR)
1. ...

### ? Não Urgente + Não Importante (ELIMINAR)
1. ...

## Ordem de Execução Recomendada
1. (maior prioridade)
2. ...
3. (menor prioridade)

## Justificativa
Explique o critério de priorização.
";

        var prioritization = await CallLLMWithRetryAsync(prompt, cancellationToken: cancellationToken);
        
        context.SharedState["Prioritization"] = prioritization;
        
        return prioritization;
    }

    /// <summary>
    /// Aprova ou rejeita ações propostas
    /// </summary>
    private async Task<string> ApproveActionsAsync(TaskStep step, TaskExecutionContext context, CancellationToken cancellationToken)
    {
        var actions = step.Parameters.TryGetValue("actions", out var a) ? a.ToString() : 
                      string.Join("\n", context.Plan?.Steps.Select(s => $"- {s.AgentName}: {s.Action}") ?? Array.Empty<string>());

        var prompt = $@"
Você é um supervisor com autoridade para aprovar ou rejeitar ações.

AÇÕES PROPOSTAS:
{actions}

CONTEXTO: {context.Prompt}

Para cada ação, determine:
1. **APROVADO** ? - Pode prosseguir
2. **REJEITADO** ? - Não deve ser executado
3. **MODIFICAR** ?? - Precisa de ajustes

## Análise de Cada Ação

| Ação | Decisão | Risco | Justificativa |
|------|---------|-------|---------------|
| ... | ?/?/?? | Alto/Médio/Baixo | ... |

## Ações Aprovadas
(lista final do que pode ser executado)

## Ações Rejeitadas
(e por que)

## Modificações Necessárias
(o que precisa mudar)
";

        var approval = await CallLLMWithRetryAsync(prompt, cancellationToken: cancellationToken);
        
        context.SharedState["ActionApproval"] = approval;
        
        return approval;
    }

    /// <summary>
    /// Coordena múltiplos agentes
    /// </summary>
    private async Task<string> CoordinateAsync(TaskStep step, TaskExecutionContext context, CancellationToken cancellationToken)
    {
        var prompt = $@"
Você é um supervisor coordenando uma equipe de agentes de IA especializados.

OBJETIVO: {context.Prompt}

AGENTES DISPONÍVEIS:
1. CodeGeneratorAgent - Gera código
2. ReviewerAgent - Revisa código
3. RAGAgent - Busca em documentos
4. APIIntegrationAgent - Integra APIs
5. WorkflowAgent - Automação
6. AnalystAgent - Análise de dados

TRABALHO JÁ REALIZADO:
{string.Join("\n", context.AgentResults.Select(r => $"- {r.AgentName}: {(r.Success ? "?" : "?")} {TruncateString(r.Result?.ToString(), 100)}"))}

ESTADO ATUAL:
{string.Join("\n", context.SharedState.Take(5).Select(kv => $"- {kv.Key}"))}

Forneça instruções de coordenação:

## 1. Análise da Situação
O que já foi feito e o que falta.

## 2. Próximas Ações
Quais agentes devem atuar e em que ordem.

## 3. Dependências
Quem depende de quem.

## 4. Pontos de Atenção
Riscos e como mitigar.

## 5. Critérios de Sucesso
Como saber se o objetivo foi atingido.
";

        var coordination = await CallLLMWithRetryAsync(prompt, cancellationToken: cancellationToken);
        
        context.SharedState["Coordination"] = coordination;
        
        return coordination;
    }

    /// <summary>
    /// Supervisão genérica
    /// </summary>
    private async Task<string> SuperviseAsync(TaskStep step, TaskExecutionContext context, CancellationToken cancellationToken)
    {
        var prompt = $@"
Você é um supervisor sênior de IA.

TAREFA: {step.Action}
CONTEXTO: {context.Prompt}
PARÂMETROS: {JsonSerializer.Serialize(step.Parameters)}

ESTADO DO WORKFLOW:
- Status: {context.Status}
- Agentes executados: {context.AgentResults.Count}
- Erros: {context.Errors.Count}

Forneça supervisão e orientação apropriada para a situação.
";

        var supervision = await CallLLMWithRetryAsync(prompt, cancellationToken: cancellationToken);
        
        context.SharedState["SupervisorGuidance"] = supervision;
        
        return supervision;
    }

    private string TruncateString(string? str, int maxLength)
    {
        if (string.IsNullOrEmpty(str)) return "";
        return str.Length <= maxLength ? str : str.Substring(0, maxLength) + "...";
    }
}
