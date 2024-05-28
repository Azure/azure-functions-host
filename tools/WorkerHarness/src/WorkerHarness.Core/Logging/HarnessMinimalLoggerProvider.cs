using Microsoft.Extensions.Logging;

namespace WorkerHarness.Core.Logging
{
    public class HarnessMinimalLoggerProvider : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName)
        {
            return new HarnessMinimalLogger();
        }

        public void Dispose()
        {
        }
    }

}
