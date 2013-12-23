using System;
using System.Reflection;

namespace Microsoft.WindowsAzure.Jobs
{
    /// <summary>
    /// Represents an attribute that is used to provide details about how a Windows Azure Blob is
    /// bound as a method input parameter.
    /// The method parameter type can be a Stream, TextWriter, string (declared as "out"), CloudBlob, ICloudBlob, CloudPageBlob, or CloudBlockBlob.
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter)]
    public class BlobOutputAttribute : Attribute
    {
        /// <summary>
        /// Initializes a new instance of the BlobOutputAttribute class.
        /// </summary>
        /// <param name="containerName">Name of the container to bind to. The path can contain tokens in curly
        /// braces to indicate a named parameter from an input attribute to substitute.</param>
        public BlobOutputAttribute(string containerName)
        {
            ContainerName = containerName;
        }

        /// <summary>
        /// Gets or sets the name of the container to bind to. The path can contain tokens in curly
        /// braces to indicate a named parameter from an input attribute to substitute.
        /// </summary>
        public string ContainerName { get; set; }

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
