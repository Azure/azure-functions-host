using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Jobs
{
    // Dispensers loggers that write to a blob. 
    internal class FunctionOutputLogDispenser : IFunctionOutputLogDispenser
    {
        private readonly CloudBlobDirectory _outputLogDirectory;

        public FunctionOutputLogDispenser(CloudBlobDirectory outputLogDirectory)
        {
            _outputLogDirectory = outputLogDirectory;
        }

        public FunctionOutputLog CreateLogStream(FunctionInvokeRequest request)
        {
            return FunctionOutputLog.GetLogStream(request, _outputLogDirectory);
        }
    }
}
