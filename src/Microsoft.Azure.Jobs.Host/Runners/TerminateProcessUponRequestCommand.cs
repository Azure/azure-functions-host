using System;
using System.Diagnostics;
using Microsoft.Azure.Jobs.Host.Protocols;
using Microsoft.WindowsAzure.Storage;

namespace Microsoft.Azure.Jobs
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
                bool terminated = NativeMethods.TerminateProcess(NativeMethods.GetCurrentProcess(), 1);
                Debug.Assert(terminated);
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
            catch (StorageException exception)
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
