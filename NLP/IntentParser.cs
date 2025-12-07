using Agent.Orchestrator.Api.DTOs;
using Agent.Orchestrator.Api.Services;
using System.Text.Json;

namespace Agent.Orchestrator.Api.NLP;

/// <summary>
/// Parser de intenções usando LLM
/// </summary>
public class IntentParser : INLPInterface
{
    private readonly ILLMService _llmService;
    private readonly ILogger<IntentParser> _logger;

    public IntentParser(ILLMService llmService, ILogger<IntentParser> logger)
    {
        _llmService = llmService;
        _logger = logger;
    }

    public async Task<IntentResult> ParseIntentAsync(string userInput, CancellationToken cancellationToken = default)
    {
        var prompt = $@"
Você é um analisador de intenções de IA. Analise o seguinte comando do usuário e extraia informações estruturadas.

COMANDO DO USUÁRIO:
{userInput}

AGENTES DISPONÍVEIS:
- CodeGeneratorAgent: Gera código em diversas linguagens
- ReviewerAgent: Revisa e analisa código
- RAGAgent: Busca em documentos e bases de conhecimento
- APIIntegrationAgent: Integra com APIs externas
- WorkflowAgent: Executa automações e pipelines
- AnalystAgent: Analisa dados e toma decisões lógicas
- SupervisorAgent: Coordena múltiplos agentes

Retorne APENAS um JSON válido no seguinte formato:
{{
    ""objective"": ""objetivo principal claro e conciso"",
    ""subGoals"": [""sub-objetivo 1"", ""sub-objetivo 2""],
    ""parameters"": {{""param1"": ""valor1""}},
    ""constraints"": [""restrição 1"", ""restrição 2""],
    ""priority"": ""Normal"",
    ""preferredAgent"": ""nome do agente mais adequado ou null"",
    ""requiredCapabilities"": [""capability1"", ""capability2""],
    ""requiresConfirmation"": false,
    ""confidence"": 0.9
}}
";

        try
        {
            var response = await _llmService.GenerateResponseAsync(prompt, cancellationToken);
            
            var jsonStart = response.IndexOf('{');
            var jsonEnd = response.LastIndexOf('}');
            
            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var jsonStr = response.Substring(jsonStart, jsonEnd - jsonStart + 1);
                var parsed = JsonSerializer.Deserialize<IntentParseResult>(jsonStr, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (parsed != null)
                {
                    return new IntentResult
                    {
                        RawInput = userInput,
                        Objective = parsed.Objective ?? userInput,
                        SubGoals = parsed.SubGoals ?? new List<string>(),
                        Parameters = parsed.Parameters ?? new Dictionary<string, object>(),
                        Constraints = parsed.Constraints ?? new List<string>(),
                        Priority = Enum.TryParse<Priority>(parsed.Priority, true, out var p) ? p : Priority.Normal,
                        PreferredAgent = parsed.PreferredAgent,
                        RequiredCapabilities = parsed.RequiredCapabilities ?? new List<string>(),
                        RequiresConfirmation = parsed.RequiresConfirmation,
                        Confidence = parsed.Confidence
                    };
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse intent, using fallback");
        }

        // Fallback: criar intenção básica
        return new IntentResult
        {
            RawInput = userInput,
            Objective = userInput,
            SubGoals = new List<string>(),
            Priority = Priority.Normal,
            Confidence = 0.5
        };
    }

    private class IntentParseResult
    {
        public string? Objective { get; set; }
        public List<string>? SubGoals { get; set; }
        public Dictionary<string, object>? Parameters { get; set; }
        public List<string>? Constraints { get; set; }
        public string? Priority { get; set; }
        public string? PreferredAgent { get; set; }
        public List<string>? RequiredCapabilities { get; set; }
        public bool RequiresConfirmation { get; set; }
        public double Confidence { get; set; }
    }
}
