using System;

namespace Microsoft.WindowsAzure.Jobs
{
    // Input argument is currently null
    internal class NullBindResult : BindResult, ISelfWatch, IMaybeErrorBindResult
    {
        private readonly string _message;

        public NullBindResult()
            : this("Azure object not found")
        {
        }

        public NullBindResult(string message)
        {
            _message = message;
        }

        public override ISelfWatch Watcher
        {
            get
            {
                return this;
            }
        }

        public string GetStatus()
        {
            if (_message == null)
            {
                return null;
            }
            return SelfWatch.EncodeSelfWatchStatus(_message);
        }

        public bool IsErrorResult { get; set; }
    }
}
