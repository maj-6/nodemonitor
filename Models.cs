using System.Text.Json.Serialization;

namespace NodeMonitor;

public class BoardConfig
{
    public string Id { get; set; } = "";
    public string BoardType { get; set; } = "";
    public string Port { get; set; } = "";
    public int BaudRate { get; set; } = 115200;
    public string ProjectPath { get; set; } = "";
    public string Environment { get; set; } = "";
}

public class AppConfig
{
    public string PlatformIOPath { get; set; } = "";
    public List<BoardConfig> Boards { get; set; } = new();
    public List<string> RecentProjects { get; set; } = new();
}

public class SerialMessage
{
    public DateTime Timestamp { get; set; }
    public string Port { get; set; } = "";
    public string Text { get; set; } = "";
    public MessageType Type { get; set; }
}

public enum MessageType
{
    Normal,
    Info,
    Warning,
    Error,
    Identification
}

public class BoardIdentification
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";
    
    [JsonPropertyName("board")]
    public string Board { get; set; } = "";
}

public class BuildResult
{
    public bool Success { get; set; }
    public string Output { get; set; } = "";
    public DateTime Timestamp { get; set; }
}
