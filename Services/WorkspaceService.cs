namespace Agent.Orchestrator.Api.Services;

public class WorkspaceService : IWorkspaceService
{
    private readonly string _workspacePath;

    public WorkspaceService()
    {
        _workspacePath = Path.Combine(Directory.GetCurrentDirectory(), "workspace");
        Directory.CreateDirectory(_workspacePath);
    }

    public async Task SaveFileAsync(string fileName, string content)
    {
        var filePath = Path.Combine(_workspacePath, fileName);
        var directory = Path.GetDirectoryName(filePath);
        
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(filePath, content);
    }

    public async Task<string> ReadFileAsync(string fileName)
    {
        var filePath = Path.Combine(_workspacePath, fileName);
        
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Arquivo não encontrado: {fileName}");
        }

        return await File.ReadAllTextAsync(filePath);
    }

    public string GetWorkspacePath() => _workspacePath;
}