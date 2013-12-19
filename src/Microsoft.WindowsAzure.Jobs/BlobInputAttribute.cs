using System;
using System.Reflection;

namespace Microsoft.WindowsAzure.Jobs
{
    // $$$ Only read these once:
    // - orchestration, for knowing what to listen on and when to run it
    // - invoker, for knowing how to bind the parameters
    [AttributeUsage(AttributeTargets.Parameter)]
    public class BlobInputAttribute : Attribute
    {
        public string ContainerName { get; set; }

        public BlobInputAttribute(string containerName)
        {
            this.ContainerName = containerName;
        }

        public static BlobInputAttribute Build(CustomAttributeData attr)
        {
            if (attr.Constructor.DeclaringType.FullName != typeof(BlobInputAttribute).FullName)
            {
                return null;
            }

            string arg = (string)attr.ConstructorArguments[0].Value;
            return new BlobInputAttribute(arg);
        }

        public override string ToString()
        {
            return string.Format("[BlobInput({0})]", ContainerName);
        }
    }
}
