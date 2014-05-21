using System;

namespace Microsoft.Azure.Jobs
{
    /// <summary>
    /// Represents an attribute that binds a parameter to an Azure Queue message, causing the method to run when a
    /// message is enqueued.
    /// </summary>
    /// <remarks>The method parameter type can be a user-defined type, or a string, object, or byte array.</remarks>
    [AttributeUsage(AttributeTargets.Parameter)]
    public sealed class QueueTriggerAttribute : Attribute
    {
        private readonly string _queueName;

        /// <summary>Initializes a new instance of the <see cref="QueueTriggerAttribute"/> class.</summary>
        /// <param name="queueName">The name of the queue to which to bind.</param>
        public QueueTriggerAttribute(string queueName)
        {
            _queueName = queueName;
        }

        /// <summary>Gets the name of the queue to which to bind.</summary>
        public string QueueName
        {
            get { return _queueName; }
        }
    }
}
