using Microsoft.Azure.Jobs.Host.Executors;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Jobs.Host.Loggers
{
    // Dispensers loggers that write to a blob. 
    internal class FunctionOutputLogDispenser : IFunctionOutputLogDispenser
    {
        private readonly CloudBlobDirectory _outputLogDirectory;

        public FunctionOutputLogDispenser(CloudBlobDirectory outputLogDirectory)
        {
            _outputLogDirectory = outputLogDirectory;
        }

        public FunctionOutputLog CreateLogStream(IFunctionInstance instance)
        {
            return FunctionOutputLog.GetLogStream(instance, _outputLogDirectory);
        }
    }
}
