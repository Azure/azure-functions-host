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

        private static readonly string[] LineDelimiter = { Environment.NewLine };
        public string GetStatus()
        {
            return string.Join("; ", _message.Split(LineDelimiter, StringSplitOptions.None));
        }

        public bool IsErrorResult { get; set; }
    }
}
