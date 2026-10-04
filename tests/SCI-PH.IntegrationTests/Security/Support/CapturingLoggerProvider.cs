using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace SCI_PH.IntegrationTests.Security.Support
{
    // Collects every log line (with exception text) so tests can assert that no secret was logged.
    internal sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> _lines = new();

        public string Text => string.Join('\n', _lines);

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(this);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(CapturingLoggerProvider provider) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                provider._lines.Enqueue($"{formatter(state, exception)} {exception}");
        }
    }
}
