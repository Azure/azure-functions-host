namespace Microsoft.Azure.Jobs
{
    /// <summary>
    /// A watcher with a static, settable status string.
    /// </summary>
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
                if (_status == null)
                {
                    return null;
                }
                return SelfWatch.EncodeSelfWatchStatus(_status);
            }
        }
    }
}