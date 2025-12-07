using Agent.Orchestrator.Api.Agents.Base;
using Agent.Orchestrator.Api.DTOs;
using Agent.Orchestrator.Api.Services;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Agent.Orchestrator.Api.Agents.Specialized;

/// <summary>
/// Agente de execução de workflows, automações e scripts
/// </summary>
public class WorkflowAgent : BaseAgent
{
    public override string Name => "WorkflowAgent";
    public override string Description => "Executa automações, scripts, pipelines e workflows";
    public override IReadOnlyList<string> Capabilities => new[]
    {
        "run-script",
        "execute-pipeline",
        "automate-task",
        "schedule-job",
        "create-workflow",
        "run-powershell",
        "run-bash"
    };

    public WorkflowAgent(
        ILLMService llmService,
        IWorkspaceService workspaceService,
        IAgentEventBus eventBus,
        ISharedMemory memory,
        ILogger<WorkflowAgent> logger)
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
                "run-script" => await RunScriptAsync(step, context, cancellationToken),
                "run-powershell" => await RunPowerShellAsync(step, context, cancellationToken),
                "run-bash" => await RunBashAsync(step, context, cancellationToken),
                "execute-pipeline" => await ExecutePipelineAsync(step, context, cancellationToken),
                "create-workflow" => await CreateWorkflowAsync(step, context, cancellationToken),
                "automate-task" => await AutomateTaskAsync(step, context, cancellationToken),
                _ => await ProcessWorkflowAsync(step, context, cancellationToken)
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
    /// Executa script genérico
    /// </summary>
    private async Task<string> RunScriptAsync(TaskStep step, TaskExecutionContext context, CancellationToken cancellationToken)
    {
        var script = step.Parameters.TryGetValue("script", out var s) ? s.ToString() : null;
        var language = step.Parameters.TryGetValue("language", out var l) ? l.ToString()?.ToLower() : "powershell";

        if (string.IsNullOrEmpty(script))
        {
            // Gerar script usando LLM
            var generatePrompt = $@"
Você é um especialista em automação e scripts.

TAREFA: {context.Prompt}
LINGUAGEM PREFERIDA: {language}

Gere um script completo e funcional que realize a tarefa solicitada.
Inclua:
1. Comentários explicativos
2. Tratamento de erros
3. Logs de progresso
4. Validações de entrada
";
            script = await CallLLMWithRetryAsync(generatePrompt, cancellationToken: cancellationToken);
        }

        // Salvar script
        var extension = language switch
        {
            "bash" or "sh" => ".sh",
            "python" or "py" => ".py",
            "javascript" or "js" => ".js",
            _ => ".ps1"
        };

        var fileName = $"script_{DateTime.Now:yyyyMMdd_HHmmss}{extension}";
        await _workspaceService.SaveFileAsync(fileName, script);
        LogStep(context, $"?? Script salvo: {fileName}");

        // Executar se for PowerShell e tiver permissão
        if (language == "powershell" && step.Parameters.TryGetValue("execute", out var exec) && exec?.ToString() == "true")
        {
            return await RunPowerShellAsync(new TaskStep { Parameters = new Dictionary<string, object> { ["script"] = script } }, context, cancellationToken);
        }

        context.SharedState["GeneratedScript"] = script;
        return $"## Script Gerado ({language})\n\n```{language}\n{script}\n```\n\n?? Salvo em: {fileName}";
    }

    /// <summary>
    /// Executa PowerShell script
    /// </summary>
    private async Task<string> RunPowerShellAsync(TaskStep step, TaskExecutionContext context, CancellationToken cancellationToken)
    {
        var script = step.Parameters.TryGetValue("script", out var s) ? s.ToString() : null;

        if (string.IsNullOrEmpty(script))
        {
            return "? Nenhum script PowerShell fornecido.";
        }

        // Por segurança, apenas gerar o comando, não executar
        var safeCommands = new[] { "Get-", "Write-", "Test-", "Select-", "Format-", "ConvertTo-", "ConvertFrom-" };
        var isSafe = safeCommands.Any(cmd => script.StartsWith(cmd, StringComparison.OrdinalIgnoreCase));

        var result = new StringBuilder();
        result.AppendLine("## PowerShell Script");
        result.AppendLine();
        result.AppendLine("```powershell");
        result.AppendLine(script);
        result.AppendLine("```");
        result.AppendLine();

        if (isSafe)
        {
            result.AppendLine("? Este script parece seguro para execução.");
            result.AppendLine();
            result.AppendLine("Para executar, use:");
            result.AppendLine("```powershell");
            result.AppendLine($"# Salvar como script.ps1 e executar:");
            result.AppendLine($"./script.ps1");
            result.AppendLine("```");
        }
        else
        {
            result.AppendLine("?? Este script requer revisão antes da execução.");
            result.AppendLine("Por segurança, scripts potencialmente destrutivos não são executados automaticamente.");
        }

        return result.ToString();
    }

    /// <summary>
    /// Gera script Bash
    /// </summary>
    private async Task<string> RunBashAsync(TaskStep step, TaskExecutionContext context, CancellationToken cancellationToken)
    {
        var prompt = $@"
Você é um especialista em scripts Bash/Shell.

TAREFA: {context.Prompt}

Gere um script Bash completo que:
1. Seja compatível com Linux/macOS
2. Tenha tratamento de erros (set -e, trap)
3. Use variáveis bem nomeadas
4. Tenha comentários explicativos
5. Faça validação de pré-requisitos
";

        var script = await CallLLMWithRetryAsync(prompt, cancellationToken: cancellationToken);
        
        var fileName = $"script_{DateTime.Now:yyyyMMdd_HHmmss}.sh";
        await _workspaceService.SaveFileAsync(fileName, script);

        context.SharedState["BashScript"] = script;

        return $"## Script Bash Gerado\n\n{script}\n\n?? Salvo em: {fileName}";
    }

    /// <summary>
    /// Cria definição de pipeline CI/CD
    /// </summary>
    private async Task<string> ExecutePipelineAsync(TaskStep step, TaskExecutionContext context, CancellationToken cancellationToken)
    {
        var pipelineType = step.Parameters.TryGetValue("type", out var t) ? t.ToString() : "github-actions";

        var prompt = $@"
Você é um especialista em CI/CD e DevOps.

TAREFA: {context.Prompt}
TIPO DE PIPELINE: {pipelineType}

Gere uma configuração completa de pipeline que inclua:
1. Build e compilação
2. Testes automatizados
3. Análise de código (linting)
4. Deploy (staging e produção)
5. Notificações
6. Rollback automático

Use o formato correto para {pipelineType}:
- github-actions: .github/workflows/main.yml
- azure-devops: azure-pipelines.yml
- gitlab-ci: .gitlab-ci.yml
- jenkins: Jenkinsfile
";

        var pipeline = await CallLLMWithRetryAsync(prompt, cancellationToken: cancellationToken);
        
        var fileName = pipelineType switch
        {
            "github-actions" => ".github/workflows/main.yml",
            "azure-devops" => "azure-pipelines.yml",
            "gitlab-ci" => ".gitlab-ci.yml",
            "jenkins" => "Jenkinsfile",
            _ => "pipeline.yml"
        };

        await _workspaceService.SaveFileAsync(fileName, pipeline);
        context.SharedState["Pipeline"] = pipeline;

        return $"## Pipeline {pipelineType}\n\n{pipeline}\n\n?? Salvo em: {fileName}";
    }

    /// <summary>
    /// Cria definição de workflow
    /// </summary>
    private async Task<string> CreateWorkflowAsync(TaskStep step, TaskExecutionContext context, CancellationToken cancellationToken)
    {
        var prompt = $@"
Você é um arquiteto de workflows e automação.

TAREFA: {context.Prompt}

Crie uma definição de workflow completa incluindo:
1. Diagrama de fluxo (em texto/mermaid)
2. Etapas detalhadas
3. Pontos de decisão
4. Tratamento de erros
5. Notificações
6. Código de implementação

Formate como um documento técnico completo.
";

        var workflow = await CallLLMWithRetryAsync(prompt, cancellationToken: cancellationToken);
        
        await _workspaceService.SaveFileAsync($"workflow_{DateTime.Now:yyyyMMdd_HHmmss}.md", workflow);
        context.SharedState["Workflow"] = workflow;

        return workflow;
    }

    /// <summary>
    /// Automatiza uma tarefa
    /// </summary>
    private async Task<string> AutomateTaskAsync(TaskStep step, TaskExecutionContext context, CancellationToken cancellationToken)
    {
        var prompt = $@"
Você é um especialista em automação de tarefas.

TAREFA A AUTOMATIZAR: {context.Prompt}

Forneça:
1. Análise da tarefa e identificação de passos repetitivos
2. Script de automação (PowerShell ou Python)
3. Configuração de agendamento (cron/Task Scheduler)
4. Monitoramento e alertas
5. Documentação de uso

Seja prático e forneça código executável.
";

        var automation = await CallLLMWithRetryAsync(prompt, cancellationToken: cancellationToken);
        
        await _workspaceService.SaveFileAsync($"automation_{DateTime.Now:yyyyMMdd_HHmmss}.md", automation);
        context.SharedState["Automation"] = automation;

        return automation;
    }

    /// <summary>
    /// Processa workflow genérico - detecta automaticamente o tipo de tarefa
    /// </summary>
    private async Task<string> ProcessWorkflowAsync(TaskStep step, TaskExecutionContext context, CancellationToken cancellationToken)
    {
        var action = step.Action.ToLower();
        var stage = step.Parameters.TryGetValue("stage", out var s) ? s.ToString() : null;

        // Detectar se é uma etapa específica de pipeline
        if (action.Contains("azure") || action.Contains("pipeline") || action.Contains("devops") || 
            action.Contains("stage") || action.Contains("deploy") || stage != null)
        {
            return await CreateAzureDevOpsPipelineStepAsync(step, context, stage, cancellationToken);
        }

        if (action.Contains("github") || action.Contains("actions"))
        {
            return await CreateGitHubActionsStepAsync(step, context, cancellationToken);
        }

        // Workflow genérico
        var prompt = $@"
Você é um especialista em automação, workflows e DevOps.

TAREFA ESPECÍFICA: {step.Action}
CONTEXTO GERAL: {context.Prompt}
PARÂMETROS: {JsonSerializer.Serialize(step.Parameters)}

TRABALHO ANTERIOR (se houver):
{string.Join("\n", context.AgentResults.Select(r => $"[{r.AgentName}]: Executou com sucesso"))}

Forneça uma solução DETALHADA e PASSO A PASSO incluindo:

## 1. Análise da Tarefa
Explique o que será feito nesta etapa.

## 2. Implementação
Forneça o código/configuração completa.

## 3. Instruções de Uso
Como aplicar esta configuração.

## 4. Verificação
Como testar se funcionou.

Seja específico e prático. Forneça código real que pode ser usado.
";

        var result = await CallLLMWithRetryAsync(prompt, cancellationToken: cancellationToken);
        
        // Salvar resultado
        var fileName = $"workflow_step_{step.Order}_{DateTime.Now:HHmmss}.md";
        await _workspaceService.SaveFileAsync(fileName, result);
        LogStep(context, $"?? Etapa salva: {fileName}");
        
        context.SharedState[$"WorkflowStep_{step.Order}"] = result;
        
        return result;
    }

    /// <summary>
    /// Cria etapa específica de Azure DevOps Pipeline
    /// </summary>
    private async Task<string> CreateAzureDevOpsPipelineStepAsync(TaskStep step, TaskExecutionContext context, string? stage, CancellationToken cancellationToken)
    {
        var previousPipeline = context.SharedState.TryGetValue("Pipeline", out var p) ? p?.ToString() : null;
        var previousSteps = context.SharedState
            .Where(kv => kv.Key.StartsWith("WorkflowStep_") || kv.Key.StartsWith("PipelineStage_"))
            .Select(kv => kv.Value?.ToString())
            .Where(v => !string.IsNullOrEmpty(v))
            .ToList();

        var prompt = $@"
Você é um especialista em Azure DevOps Pipelines (azure-pipelines.yml).

TAREFA ATUAL: {step.Action}
STAGE ESPECÍFICO: {stage ?? "não especificado"}
CONTEXTO: {context.Prompt}

CONFIGURAÇÃO ANTERIOR DO PIPELINE:
{previousPipeline ?? "Nenhuma - este é o início do pipeline"}

ETAPAS JÁ CRIADAS:
{(previousSteps.Any() ? string.Join("\n---\n", previousSteps.Take(3).Select(s => s?.Substring(0, Math.Min(s.Length, 500)))) : "Nenhuma")}

Gere a configuração YAML para esta etapa específica do Azure DevOps Pipeline.

FORMATO DE RESPOSTA:

## Etapa: {step.Action}

### Descrição
Explique o que esta etapa faz.

### Configuração YAML

```yaml
# Cole aqui o YAML para esta etapa/stage
```

### Variáveis Necessárias
Liste variáveis que precisam ser configuradas.

### Pré-requisitos
O que precisa estar configurado antes.

Se for uma continuação, mostre como integrar com o pipeline existente.
";

        var result = await CallLLMWithRetryAsync(prompt, cancellationToken: cancellationToken);

        // Extrair YAML e salvar/atualizar pipeline
        var yamlContent = ExtractYamlFromResponse(result);
        if (!string.IsNullOrEmpty(yamlContent))
        {
            // Atualizar ou criar pipeline
            var existingPipeline = previousPipeline ?? "";
            var updatedPipeline = MergePipelineYaml(existingPipeline, yamlContent, stage);
            
            await _workspaceService.SaveFileAsync("azure-pipelines.yml", updatedPipeline);
            context.SharedState["Pipeline"] = updatedPipeline;
            LogStep(context, $"?? Pipeline atualizado: azure-pipelines.yml");
        }

        context.SharedState[$"PipelineStage_{stage ?? step.Order.ToString()}"] = result;
        
        return result;
    }

    /// <summary>
    /// Cria etapa específica de GitHub Actions
    /// </summary>
    private async Task<string> CreateGitHubActionsStepAsync(TaskStep step, TaskExecutionContext context, CancellationToken cancellationToken)
    {
        var prompt = $@"
Você é um especialista em GitHub Actions.

TAREFA: {step.Action}
CONTEXTO: {context.Prompt}

Gere a configuração YAML para GitHub Actions (.github/workflows/main.yml).

Inclua:
1. Trigger (on: push, pull_request, etc.)
2. Jobs com steps detalhados
3. Uso de secrets e variáveis
4. Cache para otimização
5. Matriz de build se apropriado
";

        var result = await CallLLMWithRetryAsync(prompt, cancellationToken: cancellationToken);
        
        await _workspaceService.SaveFileAsync(".github/workflows/main.yml", result);
        context.SharedState["GitHubActions"] = result;
        
        return result;
    }

    /// <summary>
    /// Extrai conteúdo YAML de uma resposta
    /// </summary>
    private string ExtractYamlFromResponse(string response)
    {
        var yamlStart = response.IndexOf("```yaml");
        var yamlEnd = response.IndexOf("```", yamlStart + 7);
        
        if (yamlStart >= 0 && yamlEnd > yamlStart)
        {
            return response.Substring(yamlStart + 7, yamlEnd - yamlStart - 7).Trim();
        }
        
        return "";
    }

    /// <summary>
    /// Mescla YAML de pipeline existente com nova configuração
    /// </summary>
    private string MergePipelineYaml(string existing, string newContent, string? stageName)
    {
        if (string.IsNullOrWhiteSpace(existing))
        {
            // Criar pipeline base se não existir
            return $@"# Azure DevOps Pipeline
# Gerado automaticamente pelo WorkflowAgent

trigger:
  - main

pool:
  vmImage: 'ubuntu-latest'

stages:
{newContent}
";
        }

        // Se já existe, adicionar novo stage
        if (!string.IsNullOrEmpty(stageName) && !existing.Contains($"stage: {stageName}"))
        {
            // Adicionar ao final
            return existing.TrimEnd() + "\n\n" + newContent;
        }

        return existing;
    }
}
