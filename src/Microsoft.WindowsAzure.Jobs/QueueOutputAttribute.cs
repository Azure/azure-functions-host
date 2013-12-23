using System;
using System.Reflection;

namespace Microsoft.WindowsAzure.Jobs
{
    /// <summary>
    /// Represents an attribute that is used to provide details about how a Windows Azure Queue is
    /// bound as a method output parameter.
    /// The method parameter type can be either a user-defined type, or a string, or byte array (declared as "out").
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Method)]
    public class QueueOutputAttribute : Attribute
    {
        /// <summary>
        /// Initializes a new instance of the QueueOutputAttribute class.
        /// </summary>
        public QueueOutputAttribute()
        {
        }

        /// <summary>
        /// Gets or sets the name of the queue to bind to. If empty, the name of the method parameter is used
        /// as the queue name.
        /// </summary>
        public string QueueName { get; set; }

        public static QueueOutputAttribute Build(CustomAttributeData attr)
        {
            if (attr.Constructor.DeclaringType.FullName != typeof(QueueOutputAttribute).FullName)
            {
                return null;
            }
            return new QueueOutputAttribute(); // $$$
        }
    }
}
