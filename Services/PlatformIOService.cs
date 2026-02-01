using System.Diagnostics;
using System.IO;

namespace NodeMonitor.Services;

public class PlatformIOService
{
    private string _pioPath = "";
    
    public event Action<string>? OutputReceived;
    public event Action<string>? ErrorReceived;
    public event Action<bool>? BuildCompleted;
    
    public string PioPath
    {
        get => _pioPath;
        set => _pioPath = value;
    }
    
    public PlatformIOService()
    {
        // Try to find PlatformIO
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var defaultPath = Path.Combine(userProfile, ".platformio", "penv", "Scripts", "pio.exe");
        
        if (File.Exists(defaultPath))
            _pioPath = defaultPath;
    }
    
    public bool IsAvailable => !string.IsNullOrEmpty(_pioPath) && File.Exists(_pioPath);
    
    public async Task<BuildResult> BuildAsync(string projectPath, string? environment = null, CancellationToken ct = default)
    {
        var args = $"run -d \"{projectPath}\"";
        if (!string.IsNullOrEmpty(environment))
            args += $" -e {environment}";
        
        return await RunPioAsync(args, ct);
    }
    
    public async Task<BuildResult> UploadAsync(string projectPath, string? port = null, string? environment = null, CancellationToken ct = default)
    {
        var args = $"run -d \"{projectPath}\" -t upload";
        if (!string.IsNullOrEmpty(environment))
            args += $" -e {environment}";
        if (!string.IsNullOrEmpty(port))
            args += $" --upload-port {port}";
        
        return await RunPioAsync(args, ct);
    }
    
    public async Task<BuildResult> CleanAsync(string projectPath, CancellationToken ct = default)
    {
        var args = $"run -d \"{projectPath}\" -t clean";
        return await RunPioAsync(args, ct);
    }
    
    public async Task<List<string>> GetEnvironmentsAsync(string projectPath)
    {
        var environments = new List<string>();
        var iniPath = Path.Combine(projectPath, "platformio.ini");
        
        if (!File.Exists(iniPath))
            return environments;
        
        try
        {
            var lines = await File.ReadAllLinesAsync(iniPath);
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("[env:") && trimmed.EndsWith("]"))
                {
                    var env = trimmed[5..^1];
                    environments.Add(env);
                }
            }
        }
        catch { }
        
        return environments;
    }
    
    private async Task<BuildResult> RunPioAsync(string args, CancellationToken ct)
    {
        if (!IsAvailable)
        {
            return new BuildResult
            {
                Success = false,
                Output = "PlatformIO not found",
                Timestamp = DateTime.Now
            };
        }
        
        var output = new List<string>();
        var success = false;
        
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = _pioPath,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            
            using var process = new Process { StartInfo = psi };
            
            process.OutputDataReceived += (s, e) =>
            {
                if (e.Data != null)
                {
                    output.Add(e.Data);
                    OutputReceived?.Invoke(e.Data);
                }
            };
            
            process.ErrorDataReceived += (s, e) =>
            {
                if (e.Data != null)
                {
                    output.Add(e.Data);
                    ErrorReceived?.Invoke(e.Data);
                }
            };
            
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            
            await process.WaitForExitAsync(ct);
            
            success = process.ExitCode == 0;
        }
        catch (OperationCanceledException)
        {
            output.Add("Operation cancelled");
        }
        catch (Exception ex)
        {
            output.Add($"Error: {ex.Message}");
        }
        
        BuildCompleted?.Invoke(success);
        
        return new BuildResult
        {
            Success = success,
            Output = string.Join(Environment.NewLine, output),
            Timestamp = DateTime.Now
        };
    }
}
