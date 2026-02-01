using System.IO.Ports;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NodeMonitor.Services;

public class SerialConnection
{
    public string Port { get; set; } = "";
    public int BaudRate { get; set; } = 115200;
    public SerialPort? Serial { get; set; }
    public bool IsConnected => Serial?.IsOpen ?? false;
    public string? IdentifiedId { get; set; }
    public string? IdentifiedBoard { get; set; }
}

public class SerialService : IDisposable
{
    private readonly Dictionary<string, SerialConnection> _connections = new();
    private readonly object _lock = new();
    
    public event Action<string, string>? DataReceived;
    public event Action<string, string, string>? BoardIdentified;
    public event Action<string, Exception>? Error;
    
    private static readonly Regex[] IdentificationPatterns = new[]
    {
        new Regex(@"\{""id"":""([^""]+)"",""board"":""([^""]+)""\}", RegexOptions.Compiled),
        new Regex(@"\[NODEID:([^:]+):([^\]]+)\]", RegexOptions.Compiled),
    };
    
    public IEnumerable<string> GetAvailablePorts()
    {
        return SerialPort.GetPortNames()
            .Where(p => !p.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p);
    }
    
    public bool Connect(string port, int baudRate = 115200)
    {
        lock (_lock)
        {
            if (_connections.ContainsKey(port))
                Disconnect(port);
            
            try
            {
                var serial = new SerialPort(port, baudRate)
                {
                    ReadTimeout = 100,
                    WriteTimeout = 1000,
                    DtrEnable = true,
                    RtsEnable = true
                };
                
                serial.DataReceived += (s, e) => OnDataReceived(port);
                serial.ErrorReceived += (s, e) => Error?.Invoke(port, new Exception(e.EventType.ToString()));
                
                serial.Open();
                
                _connections[port] = new SerialConnection
                {
                    Port = port,
                    BaudRate = baudRate,
                    Serial = serial
                };
                
                return true;
            }
            catch (Exception ex)
            {
                Error?.Invoke(port, ex);
                return false;
            }
        }
    }
    
    public void Disconnect(string port)
    {
        lock (_lock)
        {
            if (_connections.TryGetValue(port, out var conn))
            {
                try
                {
                    conn.Serial?.Close();
                    conn.Serial?.Dispose();
                }
                catch { }
                _connections.Remove(port);
            }
        }
    }
    
    public void DisconnectAll()
    {
        lock (_lock)
        {
            foreach (var port in _connections.Keys.ToList())
                Disconnect(port);
        }
    }
    
    public bool IsConnected(string port)
    {
        lock (_lock)
        {
            return _connections.TryGetValue(port, out var conn) && conn.IsConnected;
        }
    }
    
    public SerialConnection? GetConnection(string port)
    {
        lock (_lock)
        {
            return _connections.TryGetValue(port, out var conn) ? conn : null;
        }
    }
    
    public IEnumerable<SerialConnection> GetConnections()
    {
        lock (_lock)
        {
            return _connections.Values.ToList();
        }
    }
    
    private void OnDataReceived(string port)
    {
        SerialConnection? conn;
        lock (_lock)
        {
            if (!_connections.TryGetValue(port, out conn) || conn.Serial == null)
                return;
        }
        
        try
        {
            while (conn.Serial.BytesToRead > 0)
            {
                var line = conn.Serial.ReadLine().Trim();
                if (string.IsNullOrEmpty(line)) continue;
                
                DataReceived?.Invoke(port, line);
                
                // Check for board identification
                foreach (var pattern in IdentificationPatterns)
                {
                    var match = pattern.Match(line);
                    if (match.Success)
                    {
                        conn.IdentifiedId = match.Groups[1].Value;
                        conn.IdentifiedBoard = match.Groups[2].Value;
                        BoardIdentified?.Invoke(port, conn.IdentifiedId, conn.IdentifiedBoard);
                        break;
                    }
                }
            }
        }
        catch { }
    }
    
    public void Write(string port, string data)
    {
        lock (_lock)
        {
            if (_connections.TryGetValue(port, out var conn) && conn.Serial?.IsOpen == true)
            {
                conn.Serial.WriteLine(data);
            }
        }
    }
    
    public void Dispose()
    {
        DisconnectAll();
    }
}
