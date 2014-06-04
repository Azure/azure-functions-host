using System.Runtime.ExceptionServices;

namespace Microsoft.Azure.Jobs.Host.Runners
{
    internal class ExceptionDispatchInfoDelayedException : IDelayedException
    {
        private readonly ExceptionDispatchInfo _exceptionInfo;

        public ExceptionDispatchInfoDelayedException(ExceptionDispatchInfo exceptionInfo)
        {
            _exceptionInfo = exceptionInfo;
        }

        public void Throw()
        {
            _exceptionInfo.Throw();
        }
    }
}
