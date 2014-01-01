namespace Microsoft.WindowsAzure.Jobs
{
    // Input argument is currently null
    internal class NullBindResult : BindResult, ISelfWatch
    {
        private string _message;

        public NullBindResult()
        {
            _message = "Azure object not found";
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
            return _message;
        }
    }
}
