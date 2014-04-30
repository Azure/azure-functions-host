using System;

namespace Microsoft.Azure.Jobs
{
    /// <summary>
    /// Represents an attribute that is used to provide details about how a Microsoft Azure Queue is
    /// bound as a method output parameter.
    /// The method parameter type by default can be either a user-defined type, or a string, or byte array (declared as "out").
    /// To insert multiple messages to a queue use a collections of a supported parameter type.
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter)]
    public class QueueOutputAttribute : Attribute
    {
        /// <summary>
        /// Initializes a new instance of the QueueOutputAttribute class.
        /// </summary>
        public QueueOutputAttribute()
        {
        }

        /// <summary>
        /// Initializes a new instance of the QueueOutputAttribute class.
        /// </summary>
        /// <param name="queueName">The name of the queue to bind to. If empty, the 
        /// name of the method parameter is used as the queue name.</param>
        public QueueOutputAttribute(string queueName)
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
