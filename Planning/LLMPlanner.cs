using Agent.Orchestrator.Api.DTOs;
using Agent.Orchestrator.Api.Services;
using System.Text.Json;

namespace Agent.Orchestrator.Api.Planning;

/// <summary>
/// Implementação do planner usando LLM - Cria planos detalhados com múltiplas etapas
/// </summary>
public class LLMPlanner : IPlannerService
{
    private readonly ILLMService _llmService;
    private readonly IAgentEventBus _eventBus;
    private readonly ILogger<LLMPlanner> _logger;
    private readonly IConfiguration _configuration;

    public LLMPlanner(
        ILLMService llmService,
        IAgentEventBus eventBus,
        ILogger<LLMPlanner> logger,
        IConfiguration configuration)
    {
        _llmService = llmService;
        _eventBus = eventBus;
        _logger = logger;
        _configuration = configuration;
    }

    public async Task<ExecutionPlan> CreatePlanAsync(
        IntentResult intent,
        TaskExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating execution plan for objective: {Objective}", intent.Objective);
        context.Logs.Enqueue("?? Criando plano de execução detalhado...");

        var prompt = BuildPlanningPrompt(intent, context);
        var response = await _llmService.GenerateResponseAsync(prompt, cancellationToken);
        var plan = ParsePlanFromResponse(response, intent, context);

        // Validar limites de segurança
        var maxSteps = _configuration.GetValue<int>("Agents:SafetyLimits:MaxPlanSteps", 20);
        if (plan.Steps.Count > maxSteps)
        {
            plan.Steps = plan.Steps.Take(maxSteps).ToList();
            _logger.LogWarning("Plan truncated to {MaxSteps} steps", maxSteps);
        }

        // Garantir que temos pelo menos 2 etapas para tarefas complexas
        if (plan.Steps.Count < 2 && IsComplexTask(intent))
        {
            plan = CreateDetailedFallbackPlan(intent, context);
        }

        if (intent.RequiresConfirmation || ContainsSensitiveActions(plan))
        {
            plan.RequiresHumanApproval = true;
            plan.HumanApprovalReason = "Este plano contém ações que requerem aprovação humana.";
        }

        await _eventBus.PublishAsync(new OnPlanCreated
        {
            TaskId = context.TaskId,
            Plan = plan
        });

        context.Logs.Enqueue($"? Plano criado com {plan.Steps.Count} etapa(s)");
        foreach (var step in plan.Steps)
        {
            context.Logs.Enqueue($"   {step.Order}. [{step.AgentName}] {step.Action}");
        }

        return plan;
    }

    public async Task<ExecutionPlan> ReplanAsync(
        ExecutionPlan currentPlan,
        string reason,
        TaskExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Replanning due to: {Reason}", reason);
        context.Logs.Enqueue($"?? Replanejando: {reason}");
        context.ReplanCount++;

        var completedSteps = currentPlan.Steps
            .Where(s => s.Status == StepStatus.Completed)
            .ToList();

        var prompt = $@"
O plano anterior falhou ou precisa ser ajustado.

OBJETIVO ORIGINAL: {currentPlan.Objective}

MOTIVO DO REPLANEJAMENTO: {reason}

ETAPAS JÁ CONCLUÍDAS:
{string.Join("\n", completedSteps.Select(s => $"- {s.AgentName}: {s.Action} -> {s.Result}"))}

RESULTADOS DISPONÍVEIS:
{JsonSerializer.Serialize(context.SharedState, new JsonSerializerOptions { WriteIndented = true })}

Crie um NOVO plano considerando o que já foi feito e os resultados obtidos.

{GetAgentDescriptions()}

Retorne APENAS um JSON válido com o plano no formato especificado.
";

        var response = await _llmService.GenerateResponseAsync(prompt, cancellationToken);
        var newPlan = ParsePlanFromResponse(response, context.Intent ?? new IntentResult { Objective = currentPlan.Objective }, context);
        newPlan.Version = currentPlan.Version + 1;

        await _eventBus.PublishAsync(new OnReplan
        {
            TaskId = context.TaskId,
            Reason = reason,
            OldPlan = currentPlan,
            NewPlan = newPlan
        });

        context.Logs.Enqueue($"? Novo plano v{newPlan.Version} criado com {newPlan.Steps.Count} etapas");

        return newPlan;
    }

    private string BuildPlanningPrompt(IntentResult intent, TaskExecutionContext context)
    {
        return $@"
Você é um planejador de tarefas especialista. Crie um plano de execução DETALHADO com MÚLTIPLAS ETAPAS.

OBJETIVO DO USUÁRIO: {intent.Objective}

PROMPT ORIGINAL: {context.Prompt}

SUB-OBJETIVOS IDENTIFICADOS:
{string.Join("\n", intent.SubGoals.Select(g => $"- {g}"))}

PARÂMETROS:
{JsonSerializer.Serialize(intent.Parameters)}

RESTRIÇÕES:
{string.Join("\n", intent.Constraints.Select(c => $"- {c}"))}

{GetAgentDescriptions()}

INSTRUÇÕES IMPORTANTES:
1. SEMPRE crie um plano com MÚLTIPLAS ETAPAS (mínimo 3-5 etapas)
2. Cada etapa deve ser uma ação ESPECÍFICA e CLARA
3. Use diferentes agentes quando apropriado
4. Para workflows/pipelines: detalhe cada fase (análise, criação, validação, documentação)
5. Para código: inclua etapas de geração, revisão e documentação
6. NUNCA use ações genéricas como ""Execute task based on user request""
7. Cada ""action"" deve descrever EXATAMENTE o que será feito

EXEMPLO DE PLANO BEM DETALHADO para ""Criar workflow de deploy"":
{{
    ""objective"": ""Criar workflow de deploy para Azure DevOps"",
    ""mode"": ""Sequential"",
    ""steps"": [
        {{
            ""order"": 1,
            ""agentName"": ""AnalystAgent"",
            ""action"": ""Analisar requisitos do workflow de deploy e definir etapas necessárias"",
            ""parameters"": {{""type"": ""requirements-analysis""}}
        }},
        {{
            ""order"": 2,
            ""agentName"": ""WorkflowAgent"",
            ""action"": ""Criar arquivo azure-pipelines.yml com stages de Build"",
            ""parameters"": {{""stage"": ""build""}}
        }},
        {{
            ""order"": 3,
            ""agentName"": ""WorkflowAgent"",
            ""action"": ""Adicionar stage de Testes automatizados ao pipeline"",
            ""parameters"": {{""stage"": ""test""}}
        }},
        {{
            ""order"": 4,
            ""agentName"": ""WorkflowAgent"",
            ""action"": ""Configurar stage de Deploy para ambiente de Staging"",
            ""parameters"": {{""stage"": ""deploy-staging""}}
        }},
        {{
            ""order"": 5,
            ""agentName"": ""WorkflowAgent"",
            ""action"": ""Configurar stage de Deploy para ambiente de Produção com aprovação manual"",
            ""parameters"": {{""stage"": ""deploy-production""}}
        }},
        {{
            ""order"": 6,
            ""agentName"": ""ReviewerAgent"",
            ""action"": ""Revisar o pipeline completo e validar boas práticas"",
            ""parameters"": {{}}
        }},
        {{
            ""order"": 7,
            ""agentName"": ""SupervisorAgent"",
            ""action"": ""Gerar resumo executivo e documentação do workflow"",
            ""parameters"": {{""action"": ""summarize-work""}}
        }}
    ]
}}

Agora crie um plano DETALHADO para o objetivo do usuário.
Retorne APENAS o JSON válido, sem texto adicional.
";
    }

    private string GetAgentDescriptions()
    {
        return @"
AGENTES DISPONÍVEIS (use múltiplos para tarefas complexas):

1. CodeGeneratorAgent - Gera código em C#, Python, JavaScript, etc.
   USE PARA: criar classes, funções, APIs, componentes
   
2. ReviewerAgent - Revisa código e fornece feedback de qualidade
   USE PARA: revisar código gerado, sugerir melhorias, validar qualidade
   
3. RAGAgent - Busca em documentos e bases de conhecimento
   USE PARA: buscar contexto, pesquisar documentação, encontrar exemplos
   
4. APIIntegrationAgent - Integra com APIs externas
   USE PARA: testar endpoints, gerar clientes de API, fazer requisições
   
5. WorkflowAgent - Cria automações, scripts e pipelines CI/CD
   USE PARA: criar pipelines, scripts de automação, workflows de deploy
   
6. AnalystAgent - Análise de dados, decisões e relatórios
   USE PARA: analisar requisitos, comparar opções, gerar relatórios
   
7. SupervisorAgent - Coordena e valida trabalho de múltiplos agentes
   USE PARA: validar resultados, gerar resumos, controle de qualidade
";
    }

    private bool IsComplexTask(IntentResult intent)
    {
        var complexKeywords = new[] { 
            "workflow", "pipeline", "deploy", "ci/cd", "automação", "automation",
            "completo", "complete", "full", "criar e revisar", "multiple", 
            "api", "integração", "integration", "sistema", "system"
        };
        
        var text = (intent.Objective + " " + string.Join(" ", intent.SubGoals)).ToLower();
        return complexKeywords.Any(k => text.Contains(k));
    }

    private ExecutionPlan CreateDetailedFallbackPlan(IntentResult intent, TaskExecutionContext context)
    {
        var prompt = context.Prompt.ToLower();
        var plan = new ExecutionPlan
        {
            Objective = intent.Objective,
            Mode = ExecutionMode.Sequential,
            Steps = new List<TaskStep>()
        };

        // Detectar tipo de tarefa e criar plano apropriado
        if (prompt.Contains("workflow") || prompt.Contains("pipeline") || prompt.Contains("deploy") || prompt.Contains("ci/cd"))
        {
            plan.Steps.AddRange(new[]
            {
                new TaskStep { Order = 1, AgentName = "AnalystAgent", Action = "Analisar requisitos e definir estrutura do workflow" },
                new TaskStep { Order = 2, AgentName = "WorkflowAgent", Action = "Criar estrutura base do pipeline com stage de Build" },
                new TaskStep { Order = 3, AgentName = "WorkflowAgent", Action = "Adicionar stage de Testes automatizados" },
                new TaskStep { Order = 4, AgentName = "WorkflowAgent", Action = "Configurar stages de Deploy (Staging e Produção)" },
                new TaskStep { Order = 5, AgentName = "ReviewerAgent", Action = "Revisar pipeline e validar boas práticas de CI/CD" },
                new TaskStep { Order = 6, AgentName = "SupervisorAgent", Action = "Gerar documentação e resumo do workflow criado" }
            });
        }
        else if (prompt.Contains("api") || prompt.Contains("integr"))
        {
            plan.Steps.AddRange(new[]
            {
                new TaskStep { Order = 1, AgentName = "AnalystAgent", Action = "Analisar requisitos da integração" },
                new TaskStep { Order = 2, AgentName = "APIIntegrationAgent", Action = "Testar conectividade e endpoints da API" },
                new TaskStep { Order = 3, AgentName = "CodeGeneratorAgent", Action = "Gerar código cliente para a API" },
                new TaskStep { Order = 4, AgentName = "ReviewerAgent", Action = "Revisar código de integração" },
                new TaskStep { Order = 5, AgentName = "SupervisorAgent", Action = "Validar e documentar a integração" }
            });
        }
        else if (prompt.Contains("código") || prompt.Contains("code") || prompt.Contains("classe") || prompt.Contains("class"))
        {
            plan.Steps.AddRange(new[]
            {
                new TaskStep { Order = 1, AgentName = "AnalystAgent", Action = "Analisar requisitos do código a ser gerado" },
                new TaskStep { Order = 2, AgentName = "CodeGeneratorAgent", Action = "Gerar código conforme especificação" },
                new TaskStep { Order = 3, AgentName = "ReviewerAgent", Action = "Revisar qualidade e boas práticas do código" },
                new TaskStep { Order = 4, AgentName = "CodeGeneratorAgent", Action = "Aplicar melhorias sugeridas na revisão" },
                new TaskStep { Order = 5, AgentName = "SupervisorAgent", Action = "Validar resultado final e gerar documentação" }
            });
        }
        else if (prompt.Contains("analis") || prompt.Contains("relat") || prompt.Contains("report"))
        {
            plan.Steps.AddRange(new[]
            {
                new TaskStep { Order = 1, AgentName = "RAGAgent", Action = "Buscar informações e contexto relevante" },
                new TaskStep { Order = 2, AgentName = "AnalystAgent", Action = "Analisar dados e identificar padrões" },
                new TaskStep { Order = 3, AgentName = "AnalystAgent", Action = "Gerar insights e recomendações" },
                new TaskStep { Order = 4, AgentName = "AnalystAgent", Action = "Criar relatório estruturado" },
                new TaskStep { Order = 5, AgentName = "SupervisorAgent", Action = "Revisar e finalizar relatório" }
            });
        }
        else
        {
            // Plano genérico mas ainda detalhado
            plan.Steps.AddRange(new[]
            {
                new TaskStep { Order = 1, AgentName = "AnalystAgent", Action = "Analisar requisitos e definir abordagem" },
                new TaskStep { Order = 2, AgentName = intent.PreferredAgent ?? "CodeGeneratorAgent", Action = $"Executar tarefa principal: {intent.Objective}" },
                new TaskStep { Order = 3, AgentName = "ReviewerAgent", Action = "Revisar resultado e sugerir melhorias" },
                new TaskStep { Order = 4, AgentName = "SupervisorAgent", Action = "Validar trabalho e gerar resumo final" }
            });
        }

        return plan;
    }

    private ExecutionPlan ParsePlanFromResponse(string response, IntentResult intent, TaskExecutionContext context)
    {
        try
        {
            var jsonStart = response.IndexOf('{');
            var jsonEnd = response.LastIndexOf('}');
            
            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var jsonStr = response.Substring(jsonStart, jsonEnd - jsonStart + 1);
                var parsed = JsonSerializer.Deserialize<PlanParseResult>(jsonStr, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (parsed != null && parsed.Steps != null && parsed.Steps.Count > 0)
                {
                    var plan = new ExecutionPlan
                    {
                        Objective = parsed.Objective ?? intent.Objective,
                        Mode = Enum.TryParse<ExecutionMode>(parsed.Mode, true, out var m) ? m : ExecutionMode.Sequential
                    };

                    foreach (var step in parsed.Steps)
                    {
                        // Ignorar steps com ações genéricas
                        if (step.Action?.Contains("Execute task based on") == true)
                        {
                            continue;
                        }

                        plan.Steps.Add(new TaskStep
                        {
                            Order = step.Order,
                            AgentName = step.AgentName ?? "CodeGeneratorAgent",
                            Action = step.Action ?? "Executar tarefa",
                            Parameters = step.Parameters ?? new Dictionary<string, object>(),
                            DependsOn = step.DependsOn ?? new List<string>(),
                            IsConditional = step.IsConditional,
                            Condition = step.Condition
                        });
                    }

                    // Se conseguiu parsear pelo menos 2 steps, retorna o plano
                    if (plan.Steps.Count >= 2)
                    {
                        return plan;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse plan, using detailed fallback");
        }

        // Fallback: criar plano detalhado baseado no tipo de tarefa
        return CreateDetailedFallbackPlan(intent, context);
    }

    private bool ContainsSensitiveActions(ExecutionPlan plan)
    {
        var sensitiveKeywords = new[] { "delete", "remove", "deploy", "execute", "run", "api", "external", "production" };
        return plan.Steps.Any(s => 
            sensitiveKeywords.Any(k => 
                s.Action.Contains(k, StringComparison.OrdinalIgnoreCase)));
    }

    private class PlanParseResult
    {
        public string? Objective { get; set; }
        public string? Mode { get; set; }
        public List<StepParseResult>? Steps { get; set; }
    }

    private class StepParseResult
    {
        public int Order { get; set; }
        public string? AgentName { get; set; }
        public string? Action { get; set; }
        public Dictionary<string, object>? Parameters { get; set; }
        public List<string>? DependsOn { get; set; }
        public bool IsConditional { get; set; }
        public string? Condition { get; set; }
    }
}
