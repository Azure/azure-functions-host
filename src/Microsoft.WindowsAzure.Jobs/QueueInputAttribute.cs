using System;

namespace Microsoft.WindowsAzure.Jobs
{
    /// <summary>
    /// Represents an attribute that is used to provide details about how a Windows Azure Queue is
    /// bound as a method input parameter.
    /// This attribute also serves as a trigger that will run the Job function when a new message is enqueued.
    /// The method parameter type by default can be either a user-defined type, or a string, object, or byte array.
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter)]
    public class QueueInputAttribute : Attribute
    {
        /// <summary>
        /// Initializes a new instance of the QueueInputAttribute class.
        /// </summary>
        public QueueInputAttribute()
        {
            
        }

        /// <summary>
        /// Initializes a new instance of the QueueInputAttribute class.
        /// </summary>
        /// <param name="queueName">The name of the queue to bind to. If empty, the 
        /// name of the method parameter is used as the queue name.</param>
        public QueueInputAttribute(string queueName)
        {
            QueueName = queueName;
        }

        /// <summary>
        /// Gets the name of the queue to bind to. If empty, the name of the method parameter is used
        /// as the queue name.
        /// </summary>
        public string QueueName { get; private set; }
    }
}
