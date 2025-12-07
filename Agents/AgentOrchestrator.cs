using Agent.Orchestrator.Api.Services;
using Agent.Orchestrator.Api.DTOs;
using Agent.Orchestrator.Api.Agents.Base;
using Agent.Orchestrator.Api.Agents.Specialized;
using Agent.Orchestrator.Api.NLP;
using Agent.Orchestrator.Api.Planning;

namespace Agent.Orchestrator.Api.Agents;

/// <summary>
/// Orquestrador principal de agentes autônomos
/// Suporta modos: sequencial, paralelo, condicional e hierárquico
/// </summary>
public class AgentOrchestrator
{
    private readonly Dictionary<string, IAgent> _agents;
    private readonly INLPInterface _nlpInterface;
    private readonly IPlannerService _planner;
    private readonly IAgentEventBus _eventBus;
    private readonly ISharedMemory _memory;
    private readonly ILogger<AgentOrchestrator> _logger;
    private readonly IConfiguration _configuration;

    // Agentes legados para compatibilidade
    private readonly CodeGeneratorAgent _codeGenerator;
    private readonly ReviewerAgent _reviewer;

    public AgentOrchestrator(
        ILLMService llmService,
        IWorkspaceService workspaceService,
        IAgentEventBus eventBus,
        ISharedMemory memory,
        INLPInterface nlpInterface,
        IPlannerService planner,
        ILogger<AgentOrchestrator> logger,
        IConfiguration configuration,
        ILoggerFactory loggerFactory)
    {
        _eventBus = eventBus;
        _memory = memory;
        _nlpInterface = nlpInterface;
        _planner = planner;
        _logger = logger;
        _configuration = configuration;

        // Manter compatibilidade com agentes legados
        _codeGenerator = new CodeGeneratorAgent(llmService, workspaceService);
        _reviewer = new ReviewerAgent(llmService, workspaceService);

        // Registrar agentes especializados
        _agents = new Dictionary<string, IAgent>(StringComparer.OrdinalIgnoreCase)
        {
            ["RAGAgent"] = new RAGAgent(llmService, workspaceService, eventBus, memory, loggerFactory.CreateLogger<RAGAgent>()),
            ["APIIntegrationAgent"] = new APIIntegrationAgent(llmService, workspaceService, eventBus, memory, loggerFactory.CreateLogger<APIIntegrationAgent>()),
            ["WorkflowAgent"] = new WorkflowAgent(llmService, workspaceService, eventBus, memory, loggerFactory.CreateLogger<WorkflowAgent>()),
            ["AnalystAgent"] = new AnalystAgent(llmService, workspaceService, eventBus, memory, loggerFactory.CreateLogger<AnalystAgent>()),
            ["SupervisorAgent"] = new SupervisorAgent(llmService, workspaceService, eventBus, memory, loggerFactory.CreateLogger<SupervisorAgent>())
        };
    }

    /// <summary>
    /// Executa uma tarefa de forma autônoma (novo sistema)
    /// </summary>
    public async Task ExecuteAutonomousAsync(TaskExecutionContext context, CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;
        
        try
        {
            context.Status = ExecutionStatus.Planning;
            context.Logs.Enqueue("🚀 Sistema de Agentes Autônomos Iniciado");
            context.Logs.Enqueue($"📝 Task ID: {context.TaskId}");
            context.Logs.Enqueue($"💬 Prompt: {context.Prompt}");
            context.Logs.Enqueue("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");

            await _eventBus.PublishAsync(new OnStatusChanged
            {
                TaskId = context.TaskId,
                OldStatus = ExecutionStatus.Pending,
                NewStatus = ExecutionStatus.Planning
            });

            // Etapa 1: Analisar intenção do usuário
            context.Logs.Enqueue("🧠 Analisando intenção do usuário...");
            context.Intent = await _nlpInterface.ParseIntentAsync(context.Prompt, cancellationToken);
            context.Logs.Enqueue($"✅ Objetivo identificado: {context.Intent.Objective}");
            context.Logs.Enqueue($"📊 Confiança: {context.Intent.Confidence:P0}");

            // Etapa 2: Criar plano de execução
            context.Logs.Enqueue("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            context.Plan = await _planner.CreatePlanAsync(context.Intent, context, cancellationToken);
            context.Logs.Enqueue("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");

            // Verificar se precisa aprovação humana
            if (context.Plan.RequiresHumanApproval)
            {
                context.Status = ExecutionStatus.WaitingHumanApproval;
                context.Logs.Enqueue($"⏸️ Aguardando aprovação humana: {context.Plan.HumanApprovalReason}");
                
                await _eventBus.PublishAsync(new OnHumanApprovalRequired
                {
                    TaskId = context.TaskId,
                    Reason = context.Plan.HumanApprovalReason ?? "Ação sensível detectada",
                    ActionDescription = string.Join(", ", context.Plan.Steps.Select(s => s.Action))
                });

                // Em produção, aguardaria aprovação via SignalR
                // Por enquanto, continuamos automaticamente
                context.Logs.Enqueue("✅ Aprovação automática (modo desenvolvimento)");
            }

            // Etapa 3: Executar plano
            context.Status = ExecutionStatus.Executing;
            await _eventBus.PublishAsync(new OnStatusChanged
            {
                TaskId = context.TaskId,
                OldStatus = ExecutionStatus.Planning,
                NewStatus = ExecutionStatus.Executing
            });

            await ExecutePlanAsync(context, cancellationToken);

            // Etapa 4: Finalizar
            context.Status = ExecutionStatus.Completed;
            context.CompletedAt = DateTime.UtcNow;

            await _eventBus.PublishAsync(new OnWorkflowCompleted
            {
                TaskId = context.TaskId,
                Success = true,
                TotalDuration = DateTime.UtcNow - startTime,
                Results = context.AgentResults,
                Summary = $"Plano executado com sucesso em {context.Plan.Steps.Count} etapas"
            });

            context.Logs.Enqueue("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            context.Logs.Enqueue($"✅ Workflow concluído em {(DateTime.UtcNow - startTime).TotalSeconds:F1}s!");
        }
        catch (Exception ex)
        {
            context.Status = ExecutionStatus.Failed;
            context.Errors.Add(ex.Message);
            context.Logs.Enqueue($"❌ Erro no workflow: {ex.Message}");
            _logger.LogError(ex, "Error executing autonomous workflow for task {TaskId}", context.TaskId);

            await _eventBus.PublishAsync(new OnWorkflowCompleted
            {
                TaskId = context.TaskId,
                Success = false,
                TotalDuration = DateTime.UtcNow - startTime,
                Summary = $"Falha: {ex.Message}"
            });
        }
        finally
        {
            context.IsCompleted = true;
        }
    }

    /// <summary>
    /// Executa o plano de acordo com o modo de execução
    /// </summary>
    private async Task ExecutePlanAsync(TaskExecutionContext context, CancellationToken cancellationToken)
    {
        var plan = context.Plan!;
        
        context.Logs.Enqueue($"⚙️ Executando plano em modo: {plan.Mode}");

        switch (plan.Mode)
        {
            case ExecutionMode.Parallel:
                await ExecuteParallelAsync(plan, context, cancellationToken);
                break;
            case ExecutionMode.Hierarchical:
                await ExecuteHierarchicalAsync(plan, context, cancellationToken);
                break;
            case ExecutionMode.Conditional:
            case ExecutionMode.Sequential:
            default:
                await ExecuteSequentialAsync(plan, context, cancellationToken);
                break;
        }
    }

    private async Task ExecuteSequentialAsync(ExecutionPlan plan, TaskExecutionContext context, CancellationToken cancellationToken)
    {
        foreach (var step in plan.Steps.OrderBy(s => s.Order))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                context.Logs.Enqueue("⚠️ Execução cancelada pelo usuário");
                break;
            }

            await ExecuteStepAsync(step, context, cancellationToken);

            // Verificar se precisa replanejar
            if (step.Status == StepStatus.Failed && step.RetryCount >= step.MaxRetries)
            {
                var maxReplans = _configuration.GetValue<int>("Agents:MaxRetries", 3);
                if (context.ReplanCount < maxReplans)
                {
                    context.Logs.Enqueue("🔄 Iniciando replanejamento...");
                    context.Status = ExecutionStatus.Replanning;
                    
                    plan = await _planner.ReplanAsync(plan, step.Error ?? "Step failed", context, cancellationToken);
                    context.Plan = plan;
                    
                    context.Status = ExecutionStatus.Executing;
                    // Reiniciar execução do novo plano
                    await ExecuteSequentialAsync(plan, context, cancellationToken);
                    return;
                }
            }
        }
    }

    private async Task ExecuteParallelAsync(ExecutionPlan plan, TaskExecutionContext context, CancellationToken cancellationToken)
    {
        // Agrupar steps por dependências
        var independentSteps = plan.Steps.Where(s => s.DependsOn.Count == 0).ToList();
        
        // Executar steps independentes em paralelo
        var tasks = independentSteps.Select(step => ExecuteStepAsync(step, context, cancellationToken));
        await Task.WhenAll(tasks);

        // Executar steps dependentes sequencialmente
        var dependentSteps = plan.Steps.Where(s => s.DependsOn.Count > 0).OrderBy(s => s.Order);
        foreach (var step in dependentSteps)
        {
            await ExecuteStepAsync(step, context, cancellationToken);
        }
    }

    private async Task ExecuteHierarchicalAsync(ExecutionPlan plan, TaskExecutionContext context, CancellationToken cancellationToken)
    {
        // Primeiro, executar supervisor para coordenar
        var supervisorStep = plan.Steps.FirstOrDefault(s => s.AgentName == "SupervisorAgent");
        if (supervisorStep != null)
        {
            await ExecuteStepAsync(supervisorStep, context, cancellationToken);
        }

        // Depois, executar workers
        var workerSteps = plan.Steps.Where(s => s.AgentName != "SupervisorAgent").OrderBy(s => s.Order);
        foreach (var step in workerSteps)
        {
            await ExecuteStepAsync(step, context, cancellationToken);
        }

        // Por fim, supervisor valida resultados
        if (supervisorStep != null)
        {
            var validationStep = new TaskStep
            {
                AgentName = "SupervisorAgent",
                Action = "validate-results",
                Order = plan.Steps.Count + 1
            };
            await ExecuteStepAsync(validationStep, context, cancellationToken);
        }
    }

    private async Task ExecuteStepAsync(TaskStep step, TaskExecutionContext context, CancellationToken cancellationToken)
    {
        step.Status = StepStatus.Running;
        step.StartedAt = DateTime.UtcNow;
        
        context.Logs.Enqueue($"▶️ Executando: [{step.AgentName}] {step.Action}");

        try
        {
            // Verificar condição se for step condicional
            if (step.IsConditional && !string.IsNullOrEmpty(step.Condition))
            {
                // Avaliar condição (simplificado)
                context.Logs.Enqueue($"🔍 Avaliando condição: {step.Condition}");
            }

            AgentResult result;

            // Tentar usar agente especializado primeiro
            if (_agents.TryGetValue(step.AgentName, out var agent))
            {
                result = await agent.ExecuteAsync(step, context, cancellationToken);
            }
            // Fallback para agentes legados
            else if (step.AgentName.Equals("CodeGeneratorAgent", StringComparison.OrdinalIgnoreCase))
            {
                var fileName = await _codeGenerator.ExecuteAsync(context);
                result = new AgentResult
                {
                    AgentName = "CodeGeneratorAgent",
                    Result = fileName,
                    Success = true,
                    Timestamp = DateTime.UtcNow
                };
            }
            else if (step.AgentName.Equals("ReviewerAgent", StringComparison.OrdinalIgnoreCase))
            {
                var codeFile = context.GeneratedCode != null ? "GeneratedCode.cs" : "code.cs";
                var review = await _reviewer.ExecuteAsync(context, codeFile);
                result = new AgentResult
                {
                    AgentName = "ReviewerAgent",
                    Result = review,
                    Success = true,
                    Timestamp = DateTime.UtcNow
                };
            }
            else
            {
                throw new InvalidOperationException($"Agente não encontrado: {step.AgentName}");
            }

            step.Status = result.Success ? StepStatus.Completed : StepStatus.Failed;
            step.Result = result.Result?.ToString();
            step.CompletedAt = DateTime.UtcNow;
            
            context.AddResult(step.AgentName, result.Result);
            
            if (result.Success)
            {
                context.Logs.Enqueue($"✅ [{step.AgentName}] concluído com sucesso");
            }
            else
            {
                step.Error = result.Error;
                context.Logs.Enqueue($"⚠️ [{step.AgentName}] falhou: {result.Error}");
            }
        }
        catch (Exception ex)
        {
            step.Status = StepStatus.Failed;
            step.Error = ex.Message;
            step.RetryCount++;
            step.CompletedAt = DateTime.UtcNow;
            
            context.Logs.Enqueue($"❌ [{step.AgentName}] erro: {ex.Message}");
            _logger.LogError(ex, "Error executing step {StepId} with agent {AgentName}", step.StepId, step.AgentName);

            // Tentar retry se ainda tiver tentativas
            if (step.RetryCount < step.MaxRetries)
            {
                context.Logs.Enqueue($"🔄 Tentando novamente ({step.RetryCount}/{step.MaxRetries})...");
                step.Status = StepStatus.Retrying;
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, step.RetryCount)), cancellationToken);
                await ExecuteStepAsync(step, context, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Execução legada para compatibilidade com o sistema anterior
    /// </summary>
    public async Task ExecuteAsync(TaskExecutionContext context)
    {
        try
        {
            context.Logs.Enqueue("🚀 Pipeline Multi-Agente Iniciado");
            context.Logs.Enqueue($"📝 Task ID: {context.TaskId}");
            context.Logs.Enqueue($"💬 Prompt: {context.Prompt}");
            context.Logs.Enqueue("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");

            // Etapa 1: Geração de Código
            var fileName = await _codeGenerator.ExecuteAsync(context);
            
            context.Logs.Enqueue("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");

            // Etapa 2: Revisão de Código
            await _reviewer.ExecuteAsync(context, fileName);

            context.Logs.Enqueue("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            context.Logs.Enqueue("✅ Pipeline concluído com sucesso!");
        }
        catch (Exception ex)
        {
            context.Logs.Enqueue($"❌ Erro no pipeline: {ex.Message}");
            throw;
        }
        finally
        {
            context.IsCompleted = true;
        }
    }
}