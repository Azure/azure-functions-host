using System;
using System.Reflection;

namespace Microsoft.WindowsAzure.Jobs
{
    [AttributeUsage(AttributeTargets.Parameter)]
    public class QueueInputAttribute : Attribute
    {
        // If empty, infer from the name of the local 
        public string QueueName { get ;set ;}

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
