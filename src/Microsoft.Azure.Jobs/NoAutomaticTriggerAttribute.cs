using System;

namespace Microsoft.Azure.Jobs
{
    /// <summary>
    /// Represents an attribute that is used to indicate that the JobHost should not listen to
    /// this method. This can be useful to avoid the performance impact of listening on a large container
    /// or to avoid inadvertent triggering of the function.
    /// The method can be invoked explicitly using the Call method on the JobHost.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class NoAutomaticTriggerAttribute : Attribute
    {
        /// <summary>
        /// Initializes a new instance of the NoAutomaticTriggerAttribute class.
        /// </summary>
        public NoAutomaticTriggerAttribute()
        {
        }
    }
}
