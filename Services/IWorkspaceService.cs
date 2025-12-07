namespace Agent.Orchestrator.Api.Services;

public interface IWorkspaceService
{
    Task SaveFileAsync(string fileName, string content);
    Task<string> ReadFileAsync(string fileName);
    string GetWorkspacePath();
}