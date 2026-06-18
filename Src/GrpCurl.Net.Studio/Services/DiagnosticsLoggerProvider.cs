using GrpCurl.Net.Studio.ViewModels.Models.Diagnostics;
using GrpCurl.Net.Studio.ViewModels.Services;
using Microsoft.Extensions.Logging;

namespace GrpCurl.Net.Studio.Services;

/// <summary>
///     Bridges <c>Microsoft.Extensions.Logging</c> (SPEC-030 §9 "logging throughout") to the diagnostics
///     file sink, so any <see cref="ILogger" /> output is captured in Settings → Diagnostics (FR-155).
/// </summary>
internal sealed class DiagnosticsLoggerProvider(IDiagnosticsLog log) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new DiagnosticsLogger(log, categoryName);

    public void Dispose()
    {
    }

    private sealed class DiagnosticsLogger(IDiagnosticsLog log, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var message = formatter(state, exception);

            if (exception is not null)
            {
                message = $"{message} ({exception.GetType().Name}: {exception.Message})";
            }

            log.Log(Map(logLevel), category, message);
        }

        private static DiagnosticsLevel Map(LogLevel level) => level switch
        {
            LogLevel.Trace => DiagnosticsLevel.Trace,
            LogLevel.Debug => DiagnosticsLevel.Debug,
            LogLevel.Information => DiagnosticsLevel.Information,
            LogLevel.Warning => DiagnosticsLevel.Warning,
            LogLevel.Error => DiagnosticsLevel.Error,
            LogLevel.Critical => DiagnosticsLevel.Critical,
            _ => DiagnosticsLevel.Information
        };
    }
}
