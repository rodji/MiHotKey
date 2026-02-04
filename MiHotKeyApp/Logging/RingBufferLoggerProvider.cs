namespace MiHotKeyApp.Logging;

using MiHotKeyApp.Config;
using Microsoft.Extensions.Logging;

internal sealed class RingBufferLoggerProvider : ILoggerProvider
{
    private readonly RingLogBuffer _buffer;
    private volatile LoggingSection _cfg;

    public RingBufferLoggerProvider(RingLogBuffer buffer, LoggingSection cfg)
    {
        _buffer = buffer;
        _cfg = cfg;
    }

    public void UpdateConfig(LoggingSection cfg)
    {
        _cfg = cfg;
    }

    public ILogger CreateLogger(string categoryName) => new RingBufferLogger(_buffer, categoryName, () => _cfg);

    public void Dispose()
    {
    }

    private sealed class RingBufferLogger : ILogger
    {
        private readonly RingLogBuffer _buffer;
        private readonly string _category;
        private readonly Func<LoggingSection> _getCfg;

        public RingBufferLogger(RingLogBuffer buffer, string category, Func<LoggingSection> getCfg)
        {
            _buffer = buffer;
            _category = category;
            _getCfg = getCfg;
        }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel)
        {
            if (logLevel == LogLevel.None)
            {
                return false;
            }

            var cfg = _getCfg();
            var min = cfg.Level;
            if (cfg.Overrides.TryGetValue(_category, out var overrideLevel))
            {
                min = overrideLevel;
            }

            return logLevel >= min;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var cfg = _getCfg();
            var message = formatter(state, exception);
            if (exception is not null)
            {
                message = $"{message} err=\"{exception.GetType().Name}: {exception.Message}\"";
            }

            if (cfg.MaxMessageLength > 0 && message.Length > cfg.MaxMessageLength)
            {
                message = message[..cfg.MaxMessageLength] + "…";
            }

            var line = LogLineFormatter.FormatLine(DateTimeOffset.Now, logLevel, _category, message);
            _buffer.Append(logLevel, line);
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose()
        {
        }
    }
}

