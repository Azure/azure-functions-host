using System;

namespace Microsoft.Azure.Jobs.Host.Runners
{
    internal class DelayedException : IDelayedException
    {
        private readonly Exception _exception;

        public DelayedException(Exception exception)
        {
            _exception = exception;
        }

        public void Throw()
        {
            throw _exception;
        }
    }
}
