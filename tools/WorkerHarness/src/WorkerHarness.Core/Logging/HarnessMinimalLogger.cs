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


    public class HarnessMinimalLogger : ILogger
    {
        public IDisposable BeginScope<TState>(TState state)
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            var currentTime = DateTime.Now.ToString("hh:mm:ss.fff tt");
            Console.WriteLine($"{currentTime} {formatter(state, exception)}");
        }
    }

    public static class LoggerExtensions
    {
        public static void LogInfo(this ILogger logger, ConsoleColor color, string message, params object[] args)
        {
            var originalColor = Console.ForegroundColor;
            Console.ForegroundColor = color;

            try
            {
                logger.Log(LogLevel.Information, message, args);
            }
            finally
            {
                Console.ForegroundColor = originalColor;
            }
        }
        public static void Log(this ILogger logger, LogLevel level, ConsoleColor color, string message, params object[] args)
        {
            var originalColor = Console.ForegroundColor;
            Console.ForegroundColor = color;

            try
            {
                logger.Log(level, message, args);
            }
            finally
            {
                Console.ForegroundColor = originalColor;
            }
        }
    }

}
