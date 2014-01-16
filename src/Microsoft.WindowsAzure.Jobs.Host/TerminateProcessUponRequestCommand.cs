using System;
using Microsoft.WindowsAzure.StorageClient;

namespace Microsoft.WindowsAzure.Jobs
{
    internal class TerminateProcessUponRequestCommand : ICanFailCommand
    {
        private readonly IProcessTerminationSignalReader _terminationSignalReader;
        private readonly Guid _hostInstanceId;

        public TerminateProcessUponRequestCommand(IProcessTerminationSignalReader terminationSignalReader,
            Guid hostInstanceId)
        {
            if (terminationSignalReader == null)
            {
                throw new ArgumentNullException("terminationSignalReader");
            }

            _terminationSignalReader = terminationSignalReader;
            _hostInstanceId = hostInstanceId;
        }

        public bool TryExecute()
        {
            bool hasTerminationRequest;
            bool succeeded = TryGetHasTerminationRequest(out hasTerminationRequest);

            if (succeeded && hasTerminationRequest)
            {
                Environment.Exit(1);
                return true;
            }

            return succeeded;
        }

        bool TryGetHasTerminationRequest(out bool hasTerminationRequest)
        {
            try
            {
                hasTerminationRequest = _terminationSignalReader.IsTerminationRequested(_hostInstanceId);
                return true;
            }
            catch (StorageClientException exception)
            {
                if (exception.IsServerSideError())
                {
                    hasTerminationRequest = false;
                    return false;
                }

                throw;
            }
        }
    }
}
