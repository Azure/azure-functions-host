using System;

namespace Microsoft.WindowsAzure.Jobs
{
    internal class TerminateProcessUponRequestHeartbeat : IHeartbeat
    {
        private readonly IProcessTerminationSignalReader _terminationSignalReader;
        private readonly Guid _hostInstanceId;

        public TerminateProcessUponRequestHeartbeat(IProcessTerminationSignalReader terminationSignalReader,
            Guid hostInstanceId)
        {
            if (terminationSignalReader == null)
            {
                throw new ArgumentNullException("terminationSignalReader");
            }

            _terminationSignalReader = terminationSignalReader;
            _hostInstanceId = hostInstanceId;
        }

        public void Beat()
        {
            if (HasTerminationRequest())
            {
                Environment.Exit(1);
            }
        }

        bool HasTerminationRequest()
        {
            return _terminationSignalReader.IsTerminationRequested(_hostInstanceId);
        }
    }
}
