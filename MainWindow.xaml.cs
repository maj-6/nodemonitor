using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using Microsoft.Win32;
using NodeMonitor.Services;

namespace NodeMonitor;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly SerialService _serial = new();
    private readonly PlatformIOService _pio = new();
    private readonly string _configPath;
    private CancellationTokenSource? _cts;
    
    private bool _isBusy;
    private string _statusText = "Ready";
    private BoardConfig? _selectedBoard;
    
    public event PropertyChangedEventHandler? PropertyChanged;
    
    public ObservableCollection<string> AvailablePorts { get; } = new();
    public ObservableCollection<int> BaudRates { get; } = new() { 9600, 19200, 38400, 57600, 115200, 230400, 460800, 921600 };
    public ObservableCollection<BoardConfig> Boards { get; } = new();
    
    // Monitor ports
    public string? SelectedPort1 { get; set; }
    public string? SelectedPort2 { get; set; }
    public string? SelectedPort3 { get; set; }
    public string? SelectedPort4 { get; set; }
    public int BaudRate1 { get; set; } = 115200;
    public int BaudRate2 { get; set; } = 115200;
    public int BaudRate3 { get; set; } = 115200;
    public int BaudRate4 { get; set; } = 115200;
    
    public string Connect1Text => _serial.IsConnected(SelectedPort1 ?? "") ? "Disconnect" : "Connect";
    public string Connect2Text => _serial.IsConnected(SelectedPort2 ?? "") ? "Disconnect" : "Connect";
    public string Connect3Text => _serial.IsConnected(SelectedPort3 ?? "") ? "Disconnect" : "Connect";
    public string Connect4Text => _serial.IsConnected(SelectedPort4 ?? "") ? "Disconnect" : "Connect";
    
    public bool IsBusy
    {
        get => _isBusy;
        set { _isBusy = value; OnPropertyChanged(); }
    }
    
    public string StatusText
    {
        get => _statusText;
        set { _statusText = value; OnPropertyChanged(); }
    }
    
    public BoardConfig? SelectedBoard
    {
        get => _selectedBoard;
        set { _selectedBoard = value; OnPropertyChanged(); }
    }
    
    public string PlatformIOPath
    {
        get => _pio.PioPath;
        set { _pio.PioPath = value; OnPropertyChanged(); OnPropertyChanged(nameof(PioStatus)); }
    }
    
    public string PioStatus => _pio.IsAvailable ? "PIO: OK" : "PIO: Not Found";
    public string ConnectionStatus => $"Connected: {_serial.GetConnections().Count()}";
    
    // Commands
    public ICommand Connect1Command => new RelayCommand(() => ToggleConnect(1));
    public ICommand Connect2Command => new RelayCommand(() => ToggleConnect(2));
    public ICommand Connect3Command => new RelayCommand(() => ToggleConnect(3));
    public ICommand Connect4Command => new RelayCommand(() => ToggleConnect(4));
    public ICommand BuildCommand => new RelayCommand(async () => await BuildSelectedAsync(), () => !IsBusy && SelectedBoard != null);
    public ICommand UploadCommand => new RelayCommand(async () => await UploadSelectedAsync(), () => !IsBusy && SelectedBoard != null);
    public ICommand BuildAllCommand => new RelayCommand(async () => await BuildAllAsync(), () => !IsBusy);
    public ICommand UploadAllCommand => new RelayCommand(async () => await UploadAllAsync(), () => !IsBusy);
    public ICommand CancelCommand => new RelayCommand(() => _cts?.Cancel(), () => IsBusy);
    public ICommand AddBoardCommand => new RelayCommand(AddBoard);
    public ICommand RemoveBoardCommand => new RelayCommand(RemoveBoard, () => SelectedBoard != null);
    public ICommand SaveConfigCommand => new RelayCommand(SaveConfig);
    
    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        
        _configPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NodeMonitor", "config.json");
        
        LoadConfig();
        RefreshPorts();
        
        _serial.DataReceived += OnSerialDataReceived;
        _serial.BoardIdentified += OnBoardIdentified;
        _serial.Error += OnSerialError;
        
        _pio.OutputReceived += s => Dispatcher.Invoke(() => AppendBuildOutput(s));
        _pio.ErrorReceived += s => Dispatcher.Invoke(() => AppendBuildOutput(s));
        
        // Refresh ports periodically
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        timer.Tick += (s, e) => RefreshPorts();
        timer.Start();
    }
    
    private void RefreshPorts()
    {
        var ports = _serial.GetAvailablePorts().ToList();
        var current = new[] { SelectedPort1, SelectedPort2, SelectedPort3, SelectedPort4 };
        
        AvailablePorts.Clear();
        foreach (var p in ports)
            AvailablePorts.Add(p);
        
        OnPropertyChanged(nameof(ConnectionStatus));
    }
    
    private void ToggleConnect(int monitor)
    {
        var port = monitor switch { 1 => SelectedPort1, 2 => SelectedPort2, 3 => SelectedPort3, 4 => SelectedPort4, _ => null };
        var baud = monitor switch { 1 => BaudRate1, 2 => BaudRate2, 3 => BaudRate3, 4 => BaudRate4, _ => 115200 };
        
        if (string.IsNullOrEmpty(port)) return;
        
        if (_serial.IsConnected(port))
        {
            _serial.Disconnect(port);
            StatusText = $"Disconnected {port}";
        }
        else
        {
            if (_serial.Connect(port, baud))
                StatusText = $"Connected {port}";
            else
                StatusText = $"Failed to connect {port}";
        }
        
        OnPropertyChanged($"Connect{monitor}Text");
        OnPropertyChanged(nameof(ConnectionStatus));
    }
    
    private void OnSerialDataReceived(string port, string data)
    {
        Dispatcher.Invoke(() =>
        {
            var monitor = GetMonitorForPort(port);
            if (monitor != null)
            {
                monitor.AppendText($"{data}\n");
                monitor.ScrollToEnd();
            }
        });
    }
    
    private void OnBoardIdentified(string port, string id, string board)
    {
        Dispatcher.Invoke(() =>
        {
            var label = GetLabelForPort(port);
            if (label != null)
                label.Text = $"{id} ({board})";
            
            StatusText = $"Identified {id} on {port}";
            
            // Auto-associate with configured board
            var config = Boards.FirstOrDefault(b => b.Id == id || b.BoardType == board);
            if (config != null && string.IsNullOrEmpty(config.Port))
            {
                config.Port = port;
            }
        });
    }
    
    private void OnSerialError(string port, Exception ex)
    {
        Dispatcher.Invoke(() =>
        {
            StatusText = $"Error on {port}: {ex.Message}";
        });
    }
    
    private TextBox? GetMonitorForPort(string port)
    {
        if (port == SelectedPort1) return Monitor1;
        if (port == SelectedPort2) return Monitor2;
        if (port == SelectedPort3) return Monitor3;
        if (port == SelectedPort4) return Monitor4;
        return null;
    }
    
    private TextBlock? GetLabelForPort(string port)
    {
        if (port == SelectedPort1) return Monitor1Label;
        if (port == SelectedPort2) return Monitor2Label;
        if (port == SelectedPort3) return Monitor3Label;
        if (port == SelectedPort4) return Monitor4Label;
        return null;
    }
    
    private void AppendBuildOutput(string text)
    {
        BuildOutput.AppendText($"{text}\n");
        BuildOutput.ScrollToEnd();
    }
    
    private async Task BuildSelectedAsync()
    {
        if (SelectedBoard == null || string.IsNullOrEmpty(SelectedBoard.ProjectPath)) return;
        
        IsBusy = true;
        _cts = new CancellationTokenSource();
        StatusText = $"Building {SelectedBoard.Id}...";
        BuildOutput.Clear();
        
        try
        {
            // Disconnect port before upload
            if (!string.IsNullOrEmpty(SelectedBoard.Port) && _serial.IsConnected(SelectedBoard.Port))
                _serial.Disconnect(SelectedBoard.Port);
            
            var result = await _pio.BuildAsync(SelectedBoard.ProjectPath, SelectedBoard.Environment, _cts.Token);
            StatusText = result.Success ? $"Build succeeded: {SelectedBoard.Id}" : $"Build failed: {SelectedBoard.Id}";
        }
        finally
        {
            IsBusy = false;
            _cts = null;
        }
    }
    
    private async Task UploadSelectedAsync()
    {
        if (SelectedBoard == null || string.IsNullOrEmpty(SelectedBoard.ProjectPath)) return;
        
        IsBusy = true;
        _cts = new CancellationTokenSource();
        StatusText = $"Uploading to {SelectedBoard.Id}...";
        BuildOutput.Clear();
        
        try
        {
            // Disconnect port before upload
            if (!string.IsNullOrEmpty(SelectedBoard.Port) && _serial.IsConnected(SelectedBoard.Port))
                _serial.Disconnect(SelectedBoard.Port);
            
            var result = await _pio.UploadAsync(SelectedBoard.ProjectPath, SelectedBoard.Port, SelectedBoard.Environment, _cts.Token);
            StatusText = result.Success ? $"Upload succeeded: {SelectedBoard.Id}" : $"Upload failed: {SelectedBoard.Id}";
            
            // Reconnect after upload
            if (result.Success && !string.IsNullOrEmpty(SelectedBoard.Port))
            {
                await Task.Delay(2000);
                _serial.Connect(SelectedBoard.Port, SelectedBoard.BaudRate);
            }
        }
        finally
        {
            IsBusy = false;
            _cts = null;
        }
    }
    
    private async Task BuildAllAsync()
    {
        IsBusy = true;
        _cts = new CancellationTokenSource();
        BuildOutput.Clear();
        
        try
        {
            foreach (var board in Boards.Where(b => !string.IsNullOrEmpty(b.ProjectPath)))
            {
                if (_cts.Token.IsCancellationRequested) break;
                StatusText = $"Building {board.Id}...";
                await _pio.BuildAsync(board.ProjectPath, board.Environment, _cts.Token);
            }
            StatusText = "Build all completed";
        }
        finally
        {
            IsBusy = false;
            _cts = null;
        }
    }
    
    private async Task UploadAllAsync()
    {
        IsBusy = true;
        _cts = new CancellationTokenSource();
        BuildOutput.Clear();
        
        // Disconnect all
        _serial.DisconnectAll();
        
        try
        {
            foreach (var board in Boards.Where(b => !string.IsNullOrEmpty(b.ProjectPath) && !string.IsNullOrEmpty(b.Port)))
            {
                if (_cts.Token.IsCancellationRequested) break;
                StatusText = $"Uploading to {board.Id}...";
                await _pio.UploadAsync(board.ProjectPath, board.Port, board.Environment, _cts.Token);
            }
            StatusText = "Upload all completed";
        }
        finally
        {
            IsBusy = false;
            _cts = null;
        }
    }
    
    private void AddBoard()
    {
        var board = new BoardConfig { Id = $"Board{Boards.Count + 1}", BaudRate = 115200 };
        Boards.Add(board);
        SelectedBoard = board;
    }
    
    private void RemoveBoard()
    {
        if (SelectedBoard != null)
        {
            Boards.Remove(SelectedBoard);
            SelectedBoard = Boards.FirstOrDefault();
        }
    }
    
    private void LoadConfig()
    {
        try
        {
            if (File.Exists(_configPath))
            {
                var json = File.ReadAllText(_configPath);
                var config = JsonSerializer.Deserialize<AppConfig>(json);
                if (config != null)
                {
                    if (!string.IsNullOrEmpty(config.PlatformIOPath))
                        _pio.PioPath = config.PlatformIOPath;
                    
                    foreach (var b in config.Boards)
                        Boards.Add(b);
                }
            }
        }
        catch { }
    }
    
    private void SaveConfig()
    {
        try
        {
            var dir = Path.GetDirectoryName(_configPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            
            var config = new AppConfig
            {
                PlatformIOPath = _pio.PioPath,
                Boards = Boards.ToList()
            };
            
            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_configPath, json);
            StatusText = "Configuration saved";
        }
        catch (Exception ex)
        {
            StatusText = $"Save failed: {ex.Message}";
        }
    }
    
    // UI Event Handlers
    private void ClearMonitor1_Click(object sender, RoutedEventArgs e) => Monitor1.Clear();
    private void ClearMonitor2_Click(object sender, RoutedEventArgs e) => Monitor2.Clear();
    private void ClearMonitor3_Click(object sender, RoutedEventArgs e) => Monitor3.Clear();
    private void ClearMonitor4_Click(object sender, RoutedEventArgs e) => Monitor4.Clear();
    private void ClearBuildOutput_Click(object sender, RoutedEventArgs e) => BuildOutput.Clear();
    
    private void BrowseProject_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Select PlatformIO Project" };
        if (dialog.ShowDialog() == true && SelectedBoard != null)
        {
            SelectedBoard.ProjectPath = dialog.FolderName;
            OnPropertyChanged(nameof(SelectedBoard));
        }
    }
    
    private void BrowsePio_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select PlatformIO Executable",
            Filter = "Executable|pio.exe",
            InitialDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".platformio", "penv", "Scripts")
        };
        if (dialog.ShowDialog() == true)
        {
            PlatformIOPath = dialog.FileName;
        }
    }
    
    private async void RefreshEnv_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedBoard != null && !string.IsNullOrEmpty(SelectedBoard.ProjectPath))
        {
            var envs = await _pio.GetEnvironmentsAsync(SelectedBoard.ProjectPath);
            EnvCombo.ItemsSource = envs;
            if (envs.Count > 0 && string.IsNullOrEmpty(SelectedBoard.Environment))
                SelectedBoard.Environment = envs[0];
        }
    }
    
    protected override void OnClosing(CancelEventArgs e)
    {
        _serial.Dispose();
        base.OnClosing(e);
    }
    
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

public class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;
    
    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }
    
    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }
    
    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;
    public void Execute(object? parameter) => _execute();
}
