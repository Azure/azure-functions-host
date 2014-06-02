namespace Microsoft.Azure.Jobs
{
    internal class BindResult
    {
        // The actual object passed into the user's method.
        // This can be updated on function return if the parameter is an out-parameter.
        public object Result { get; set; }

        // A self-watch on the object for providing real-time status information
        // about how that object is being used.
        public virtual ISelfWatch Watcher
        {
            get
            {
                return this.Result as ISelfWatch;
            }
        }

        // A cleanup action called for this parameter after the function returns.
        // Called in both regular and exceptional cases. 
        // This can do things like close a stream, or queue a message.
        public virtual void OnPostAction()
        {
            // default is nop
        }
    }     
}
