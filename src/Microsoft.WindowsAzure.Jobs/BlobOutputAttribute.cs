using System;
using System.Reflection;

namespace Microsoft.WindowsAzure.Jobs
{
    [AttributeUsage(AttributeTargets.Parameter)]
    public class BlobOutputAttribute : Attribute
    {
        public string ContainerName { get; set; }

        public BlobOutputAttribute(string containerName)
        {
            this.ContainerName = containerName;
        }

        public static BlobOutputAttribute Build(CustomAttributeData attr)
        {
            if (attr.Constructor.DeclaringType.FullName != typeof(BlobOutputAttribute).FullName)
            {
                return null;
            }

            string arg = (string)attr.ConstructorArguments[0].Value;
            return new BlobOutputAttribute(arg);
        }

        public override string ToString()
        {
            return string.Format("[BlobOutput({0})]", ContainerName);
        }
    }
}
