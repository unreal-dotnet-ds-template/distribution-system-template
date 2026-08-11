using Microsoft.Extensions.Logging;

namespace Dst.HostApplication.Tests;

internal sealed class XUnitLoggerProvider : ILoggerProvider
{
    private readonly ITestOutputHelper _output;
    public XUnitLoggerProvider(ITestOutputHelper output) => _output = output;
    public ILogger CreateLogger(string categoryName) => new XUnitLogger(_output, categoryName);
    public void Dispose() { }

    private sealed class XUnitLogger : ILogger
    {
        private readonly ITestOutputHelper _output;
        private readonly string _categoryName;

        public XUnitLogger(ITestOutputHelper output, string categoryName)
        {
            _output = output;
            _categoryName = categoryName;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            try
            {
                _output.WriteLine($"[{logLevel}] [{_categoryName}] {formatter(state, exception)}");
                if (exception != null)
                {
                    _output.WriteLine(exception.ToString());
                }
            }
            catch
            {
            }
        }
    }
}
