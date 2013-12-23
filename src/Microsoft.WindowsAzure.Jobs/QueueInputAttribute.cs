using System;
using System.Reflection;

namespace Microsoft.WindowsAzure.Jobs
{
    /// <summary>
    /// Represents an attribute that is used to provide details about how a Windows Azure Queue is
    /// bound as a method input parameter.
    /// The method parameter type can be either a user-defined type, or a string, object, or byte array.
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
        /// Gets or sets the name of the queue to bind to. If empty, the name of the method parameter is used
        /// as the queue name.
        /// </summary>
        public string QueueName { get; set; }

        public static QueueInputAttribute Build(CustomAttributeData attr)
        {
            if (attr.Constructor.DeclaringType.FullName != typeof(QueueInputAttribute).FullName)
            {
                return null;
            }
            return new QueueInputAttribute(); // $$$
        }
    }
}
