namespace SaleOrd.SyncAgent.Services;

public enum LogLevel { Debug, Info, Warn, Error }

public class LogEventArgs : EventArgs
{
    public DateTime Time { get; init; } = DateTime.Now;
    public LogLevel Level { get; init; }
    public string Message { get; init; } = "";
}

// Single logger shared across the agent: forwards every line to the UI (for
// the live Logs tab) and appends it to a rolling local file — the file
// matters most here since it's the only trail left when the agent can't
// even reach the configured SQL Server (local or remote) to write a
// SyncLogs row.
public class AgentLogger
{
    private readonly object _fileLock = new();
    private readonly string _logFilePath;

    public event EventHandler<LogEventArgs>? LogWritten;

    public AgentLogger(string logFilePath)
    {
        _logFilePath = logFilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(logFilePath)!);
    }

    public void Debug(string message) => Write(LogLevel.Debug, message);
    public void Info(string message) => Write(LogLevel.Info, message);
    public void Warn(string message) => Write(LogLevel.Warn, message);
    public void Error(string message) => Write(LogLevel.Error, message);
    public void Error(string message, Exception ex) => Write(LogLevel.Error, $"{message}: {ex.Message}");

    private void Write(LogLevel level, string message)
    {
        var args = new LogEventArgs { Level = level, Message = message };

        try
        {
            lock (_fileLock)
            {
                var line = $"{args.Time:yyyy-MM-dd HH:mm:ss} [{level}] {message}{Environment.NewLine}";
                File.AppendAllText(_logFilePath, line);
                TrimIfTooLarge();
            }
        }
        catch { /* logging must never crash the agent */ }

        LogWritten?.Invoke(this, args);
    }

    // Keep the log file bounded — this runs unattended indefinitely, so an
    // unbounded file would eventually fill the disk.
    private void TrimIfTooLarge()
    {
        const long maxBytes = 5 * 1024 * 1024;
        var info = new FileInfo(_logFilePath);
        if (!info.Exists || info.Length <= maxBytes) return;

        var lines = File.ReadAllLines(_logFilePath);
        var keep = lines.Skip(Math.Max(0, lines.Length - 2000));
        File.WriteAllLines(_logFilePath, keep);
    }
}
