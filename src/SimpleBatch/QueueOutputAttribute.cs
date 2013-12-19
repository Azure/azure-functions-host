using System;
using System.Reflection;

namespace Microsoft.WindowsAzure.Jobs
{
    [AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Method)]
    public class QueueOutputAttribute : Attribute
    {
        // If empty, infer from the name of the local 
        public string QueueName { get; set; }

        public QueueOutputAttribute()
        {
        }

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
