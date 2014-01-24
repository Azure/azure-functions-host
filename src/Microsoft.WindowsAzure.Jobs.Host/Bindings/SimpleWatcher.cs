using System;

namespace Microsoft.WindowsAzure.Jobs
{
    internal class SimpleWatcher : ISelfWatch
    {
        private readonly object _statusLock = new object();
        private string _status;

        public void SetStatus(string status)
        {
            lock (_statusLock)
            {
                _status = status;
            }
        }
        public string GetStatus()
        {
            lock (_statusLock)
            {
                return _status.Replace(Environment.NewLine, "; ");
            }
        }
    }
}