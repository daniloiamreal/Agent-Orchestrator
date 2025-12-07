using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Agent.Orchestrator.Api.Services;

public class LLMService : ILLMService
{
    private readonly IConfiguration _configuration;
    private readonly HttpClient _httpClient;
    private readonly bool _useMock;
    private readonly ILogger<LLMService> _logger;
    private readonly string _provider;
    private readonly string? _apiKey;

    public LLMService(IConfiguration configuration, ILogger<LLMService> logger)
    {
        _configuration = configuration;
        _logger = logger;
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(120);
        _useMock = _configuration.GetValue<bool>("UseMockLLM");
        _provider = _configuration["AI:Provider"] ?? "Gemini";
        _apiKey = _configuration["AI:ApiKey"];

        if (!_useMock)
        {
            // Gemini usa API key na URL, não no header
            if (_provider != "Gemini" && _provider != "Ollama")
            {
                if (_provider == "AzureOpenAI")
                {
                    _httpClient.DefaultRequestHeaders.Add("api-key", _apiKey);
                }
                else // OpenAI, Perplexity, AIML
                {
                    _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
                }
            }

            _logger.LogInformation("LLMService configurado com {Provider}", _provider);
        }
        else
        {
            _logger.LogInformation("LLMService configurado em modo MOCK");
        }
    }

    public async Task<string> GenerateResponseAsync(string prompt, CancellationToken cancellationToken = default)
    {
        if (_useMock)
        {
            return await GenerateMockResponseAsync(prompt);
        }

        return _provider switch
        {
            "Gemini" => await CallGeminiAsync(prompt, cancellationToken),
            "Ollama" => await CallOllamaAsync(prompt, cancellationToken),
            _ => await CallOpenAICompatibleAsync(prompt, cancellationToken)
        };
    }

    /// <summary>
    /// Chamada para Google Gemini API
    /// </summary>
    private async Task<string> CallGeminiAsync(string prompt, CancellationToken cancellationToken)
    {
        var model = _configuration["AI:Model"] ?? "gemini-1.5-flash";
        var endpoint = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={_apiKey}";

        _logger.LogInformation("Chamando Gemini - Modelo: {Model}", model);

        var requestBody = new
        {
            contents = new[]
            {
                new
                {
                    parts = new[]
                    {
                        new { text = prompt }
                    }
                }
            },
            generationConfig = new
            {
                temperature = 0.7,
                maxOutputTokens = 4096,
                topP = 0.95,
                topK = 40
            },
            safetySettings = new[]
            {
                new { category = "HARM_CATEGORY_HARASSMENT", threshold = "BLOCK_NONE" },
                new { category = "HARM_CATEGORY_HATE_SPEECH", threshold = "BLOCK_NONE" },
                new { category = "HARM_CATEGORY_SEXUALLY_EXPLICIT", threshold = "BLOCK_NONE" },
                new { category = "HARM_CATEGORY_DANGEROUS_CONTENT", threshold = "BLOCK_NONE" }
            }
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            var response = await _httpClient.PostAsync(endpoint, content, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Erro na chamada Gemini: {StatusCode} - {Error}", response.StatusCode, errorContent);
                throw new HttpRequestException($"Erro na API Gemini: {response.StatusCode} - {errorContent}");
            }

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogInformation("Resposta recebida do Gemini com sucesso");

            var jsonDoc = JsonDocument.Parse(responseBody);
            
            // Extrair texto da resposta do Gemini
            var candidates = jsonDoc.RootElement.GetProperty("candidates");
            if (candidates.GetArrayLength() > 0)
            {
                var parts = candidates[0].GetProperty("content").GetProperty("parts");
                if (parts.GetArrayLength() > 0)
                {
                    var text = parts[0].GetProperty("text").GetString();
                    return text ?? "Nenhuma resposta gerada.";
                }
            }

            return "Nenhuma resposta gerada pelo Gemini.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao chamar Gemini");
            throw;
        }
    }

    /// <summary>
    /// Chamada para Ollama (local)
    /// </summary>
    private async Task<string> CallOllamaAsync(string prompt, CancellationToken cancellationToken)
    {
        var endpoint = _configuration["AI:Endpoint"] ?? "http://localhost:11434/api/generate";
        var model = _configuration["AI:Model"] ?? "llama3";

        _logger.LogInformation("Chamando Ollama - Modelo: {Model}", model);

        var requestBody = new
        {
            model,
            prompt,
            stream = false
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            var response = await _httpClient.PostAsync(endpoint, content, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Erro na chamada Ollama: {StatusCode} - {Error}", response.StatusCode, errorContent);
                throw new HttpRequestException($"Erro na API Ollama: {response.StatusCode}");
            }

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            var jsonDoc = JsonDocument.Parse(responseBody);
            var text = jsonDoc.RootElement.GetProperty("response").GetString();

            _logger.LogInformation("Resposta recebida do Ollama com sucesso");
            return text ?? "Nenhuma resposta gerada.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao chamar Ollama");
            throw;
        }
    }

    /// <summary>
    /// Chamada para APIs compatíveis com OpenAI (OpenAI, Perplexity, AIML, Azure)
    /// </summary>
    private async Task<string> CallOpenAICompatibleAsync(string prompt, CancellationToken cancellationToken)
    {
        var endpoint = _configuration["AI:Endpoint"];
        var model = _configuration["AI:Model"];

        _logger.LogInformation("Chamando {Provider} - Modelo: {Model}", _provider, model);

        var requestBody = new
        {
            model,
            messages = new[]
            {
                new { role = "system", content = "Você é um assistente especializado em programação C# e .NET." },
                new { role = "user", content = prompt }
            },
            temperature = 0.7,
            max_tokens = 2000
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            var response = await _httpClient.PostAsync(endpoint, content, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Erro na chamada {Provider}: {StatusCode} - {Error}", _provider, response.StatusCode, errorContent);
                throw new HttpRequestException($"Erro na API {_provider}: {response.StatusCode}");
            }

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogInformation("Resposta recebida do {Provider} com sucesso", _provider);

            var jsonDoc = JsonDocument.Parse(responseBody);
            var message = jsonDoc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            return message ?? "Nenhuma resposta gerada.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao chamar {Provider}", _provider);
            throw;
        }
    }

    private async Task<string> GenerateMockResponseAsync(string prompt)
    {
        _logger.LogInformation("Gerando resposta MOCK");
        await Task.Delay(500);

        // Mock para Intent Parser
        if (prompt.Contains("analisador de intenções") || prompt.Contains("COMANDO DO USUÁRIO"))
        {
            return @"{
    ""objective"": ""Criar uma calculadora com operações básicas"",
    ""subGoals"": [""Gerar código da calculadora"", ""Revisar o código gerado""],
    ""parameters"": {},
    ""constraints"": [],
    ""priority"": ""Normal"",
    ""preferredAgent"": ""CodeGeneratorAgent"",
    ""requiredCapabilities"": [""generate-code""],
    ""requiresConfirmation"": false,
    ""confidence"": 0.95
}";
        }

        // Mock para Planner
        if (prompt.Contains("planejador de tarefas") || prompt.Contains("AGENTES DISPONÍVEIS"))
        {
            return @"{
    ""objective"": ""Criar uma calculadora com operações básicas"",
    ""mode"": ""Sequential"",
    ""steps"": [
        {
            ""order"": 1,
            ""agentName"": ""CodeGeneratorAgent"",
            ""action"": ""Gerar código da classe Calculator"",
            ""parameters"": {},
            ""dependsOn"": [],
            ""isConditional"": false,
            ""condition"": null
        },
        {
            ""order"": 2,
            ""agentName"": ""ReviewerAgent"",
            ""action"": ""Revisar e analisar o código gerado"",
            ""parameters"": {},
            ""dependsOn"": [""1""],
            ""isConditional"": false,
            ""condition"": null
        }
    ]
}";
        }

        // Mock para geração de código
        if (prompt.Contains("gerar") || prompt.Contains("código") || prompt.Contains("criar") ||
            prompt.Contains("Calculator") || prompt.Contains("calculadora") || prompt.Contains("Tarefa do usuário"))
        {
            return @"
using System;

namespace Calculator
{
    public class Calculator
    {
        public double Add(double a, double b) => a + b;
        public double Subtract(double a, double b) => a - b;
        public double Multiply(double a, double b) => a * b;
        public double Divide(double a, double b) => b != 0 ? a / b : throw new DivideByZeroException();
        public double Power(double baseNum, double exponent) => Math.Pow(baseNum, exponent);
        public double SquareRoot(double number) => number >= 0 ? Math.Sqrt(number) : throw new ArgumentException();
    }
}";
        }

        // Mock para revisão de código
        if (prompt.Contains("revisar") || prompt.Contains("analisar") || prompt.Contains("melhorar") ||
            prompt.Contains("revisor de código") || prompt.Contains("Código para revisão"))
        {
            return @"## ✅ REVISÃO DE CÓDIGO CONCLUÍDA

### Nota Final: **9/10**

**Pontos Positivos:**
- ✅ Código bem estruturado
- ✅ Métodos claros e concisos

**Sugestões:**
- Adicionar documentação XML
- Implementar testes unitários";
        }

        return "Resposta gerada pelo mock LLM.";
    }
}