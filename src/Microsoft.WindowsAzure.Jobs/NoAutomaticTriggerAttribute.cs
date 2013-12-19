using System;
using System.Reflection;

namespace Microsoft.WindowsAzure.Jobs
{
    // Tells orchestration layer to not listen on this method.
    // This can be useful to avoid the performance impact of listening on a large container. 
    // Method must be invoked explicitly.
    [AttributeUsage(AttributeTargets.Method)]
    internal class NoAutomaticTriggerAttribute : Attribute
    {
        public static NoAutomaticTriggerAttribute Build(CustomAttributeData attr)
        {
            if (attr.Constructor.DeclaringType.FullName != typeof(NoAutomaticTriggerAttribute).FullName)
            {
                return null;
            }
            return new NoAutomaticTriggerAttribute();
        } 
    }
}
