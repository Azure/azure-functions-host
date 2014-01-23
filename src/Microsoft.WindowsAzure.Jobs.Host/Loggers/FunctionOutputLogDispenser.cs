using System;
using System.IO;

namespace Microsoft.WindowsAzure.Jobs
{
    // Dispensers loggers that write to a blob. 
    internal class FunctionOutputLogDispenser : IFunctionOuputLogDispenser
    {
        private readonly IAccountInfo _accountInfo;
        private readonly Action<TextWriter> _fPAddHeaderInfo;
        private readonly string _containerName;

        public FunctionOutputLogDispenser(IAccountInfo accountInfo, Action<TextWriter> fpAddHeaderInfo, string containerName)
        {
            _accountInfo = accountInfo;
            _fPAddHeaderInfo = fpAddHeaderInfo;
            _containerName = containerName;
        }

        public FunctionOutputLog CreateLogStream(FunctionInvokeRequest request)
        {
            var logInfo = FunctionOutputLog.GetLogStream(request, _accountInfo.AccountConnectionString, _containerName);

            var x = _fPAddHeaderInfo;
            if (x != null)
            {
                _fPAddHeaderInfo(logInfo.Output);
            }
            return logInfo;
        }
    }
}
