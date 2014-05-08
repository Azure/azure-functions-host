using System.Collections.Generic;
using Microsoft.Azure.Jobs;

namespace Dashboard.Data
{
    internal class CompositeFunctionInstanceLogger : IFunctionInstanceLogger
    {
        private readonly IEnumerable<IFunctionInstanceLogger> _loggers;

        public CompositeFunctionInstanceLogger(params IFunctionInstanceLogger[] loggers)
        {
            _loggers = loggers;
        }

        public void LogFunctionStarted(ExecutionInstanceLogEntity logEntity)
        {
            foreach (IFunctionInstanceLogger logger in _loggers)
            {
                logger.LogFunctionStarted(logEntity);
            }
        }

        public void LogFunctionCompleted(ExecutionInstanceLogEntity logEntity)
        {
            foreach (IFunctionInstanceLogger logger in _loggers)
            {
                logger.LogFunctionCompleted(logEntity);
            }
        }
    }
}