using System;

namespace Microsoft.Azure.Jobs.Host.Runners
{
    internal class AggregateDelayedException : IDelayedException
    {
        private readonly AggregateException _exception;

        public AggregateDelayedException(AggregateException exception)
        {
            _exception = exception;
        }

        public void Throw()
        {
            throw _exception;
        }
    }
}
