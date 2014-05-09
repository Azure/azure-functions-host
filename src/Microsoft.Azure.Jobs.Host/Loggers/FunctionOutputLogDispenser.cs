using System;
using System.IO;
using Microsoft.WindowsAzure.Storage;

namespace Microsoft.Azure.Jobs
{
    // Dispensers loggers that write to a blob. 
    internal class FunctionOutputLogDispenser : IFunctionOuputLogDispenser
    {
        private readonly CloudStorageAccount _account;
        private readonly Action<TextWriter> _fPAddHeaderInfo;
        private readonly string _containerName;

        public FunctionOutputLogDispenser(CloudStorageAccount account, Action<TextWriter> fpAddHeaderInfo, string containerName)
        {
            _account = account;
            _fPAddHeaderInfo = fpAddHeaderInfo;
            _containerName = containerName;
        }

        public FunctionOutputLog CreateLogStream(FunctionInvokeRequest request)
        {
            var logInfo = FunctionOutputLog.GetLogStream(request, _account.ToString(exportSecrets: true), _containerName);

            var x = _fPAddHeaderInfo;
            if (x != null)
            {
                _fPAddHeaderInfo(logInfo.Output);
            }
            return logInfo;
        }
    }
}
