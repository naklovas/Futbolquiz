using Microsoft.Extensions.Logging;

namespace ITInventory.ExpirationNotifier.Logging;

/// <summary>
/// Plain per-day text log under logs\ next to the executable -- a Windows Service has no
/// console to watch, so this is how a non-technical admin checks what the service did.
/// </summary>
public class FileLoggerProvider : ILoggerProvider
{
    private readonly string _logDirectory;
    private readonly object _writeLock = new();

    public FileLoggerProvider(string logDirectory)
    {
        _logDirectory = logDirectory;
        Directory.CreateDirectory(_logDirectory);
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName, _logDirectory, _writeLock);

    public void Dispose() { }

    private class FileLogger : ILogger
    {
        private readonly string _categoryName;
        private readonly string _logDirectory;
        private readonly object _writeLock;

        public FileLogger(string categoryName, string logDirectory, object writeLock)
        {
            _categoryName = categoryName;
            _logDirectory = logDirectory;
            _writeLock = writeLock;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{logLevel}] {_categoryName}: {formatter(state, exception)}";
            if (exception != null) line += Environment.NewLine + exception;

            var path = Path.Combine(_logDirectory, $"expiration-notifier-{DateTime.Now:yyyy-MM-dd}.log");
            lock (_writeLock)
            {
                File.AppendAllText(path, line + Environment.NewLine);
            }
        }
    }
}
