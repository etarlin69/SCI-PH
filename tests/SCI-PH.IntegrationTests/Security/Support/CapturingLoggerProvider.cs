using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace SCI_PH.IntegrationTests.Security.Support
{
    // Collects every log line (with exception text) so tests can assert that no secret was logged.
    internal sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> _lines = new();

        public string Text => string.Join('\n', _lines);

        // The request log line carries a random hex X-Correlation-ID, which can contain a short number (a slot) by chance:
        // tests looking for such numbers use this text, with every correlation id masked.
        public string TextWithoutCorrelationIds =>
            System.Text.RegularExpressions.Regex.Replace(Text, "CorrelationId=[0-9a-fA-F]+", "CorrelationId=<id>");

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
