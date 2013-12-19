namespace Microsoft.WindowsAzure.Jobs
{
    // Input argument is currently null
    class NullBindResult : BindResult, ISelfWatch
    {
        string message;

        public NullBindResult()
        {
            this.message = "Azure object not found";
        }

        public NullBindResult(string message)
        {
            this.message = message;
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
            return this.message;
        }
    }
}
