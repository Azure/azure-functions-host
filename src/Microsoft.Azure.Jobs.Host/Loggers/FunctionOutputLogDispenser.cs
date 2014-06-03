using Microsoft.WindowsAzure.Storage;

namespace Microsoft.Azure.Jobs
{
    // Dispensers loggers that write to a blob. 
    internal class FunctionOutputLogDispenser : IFunctionOuputLogDispenser
    {
        private readonly CloudStorageAccount _account;
        private readonly string _containerName;

        public FunctionOutputLogDispenser(CloudStorageAccount account, string containerName)
        {
            _account = account;
            _containerName = containerName;
        }

        public FunctionOutputLog CreateLogStream(FunctionInvokeRequest request)
        {
            return FunctionOutputLog.GetLogStream(request, _account.ToString(exportSecrets: true), _containerName);
        }
    }
}
