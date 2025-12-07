namespace Agent.Orchestrator.Api.DTOs;

/// <summary>
/// Resultado da análise de intenção do NLP
/// </summary>
public class IntentResult
{
    public string RawInput { get; set; } = string.Empty;
    public string Objective { get; set; } = string.Empty;
    public List<string> SubGoals { get; set; } = new();
    public Dictionary<string, object> Parameters { get; set; } = new();
    public List<string> Constraints { get; set; } = new();
    public Priority Priority { get; set; } = Priority.Normal;
    public string? PreferredAgent { get; set; }
    public List<string> RequiredCapabilities { get; set; } = new();
    public double Confidence { get; set; }
    public bool RequiresConfirmation { get; set; } = false;
}

public enum Priority
{
    Low = 0,
    Normal = 1,
    High = 2,
    Critical = 3
}
