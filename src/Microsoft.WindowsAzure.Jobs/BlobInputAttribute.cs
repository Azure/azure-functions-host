using System;
using System.Reflection;

namespace Microsoft.WindowsAzure.Jobs
{
    // $$$ Only read these once:
    // - orchestration, for knowing what to listen on and when to run it
    // - invoker, for knowing how to bind the parameters
    /// <summary>
    /// Represents an attribute that is used to provide details about how a Windows Azure Blob is
    /// bound as a method input parameter.
    /// The method parameter type can be a Stream, TextReader, string, CloudBlob, ICloudBlob, CloudPageBlob, or CloudBlockBlob.
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter)]
    public class BlobInputAttribute : Attribute
    {
        /// <summary>
        /// Initializes a new instance of the BlobInputAttribute class.
        /// </summary>
        /// <param name="containerName">Name of the container to bind to. The path can contain tokens in curly
        /// braces to indicate a pattern to match. The matched name can be used in other binding attributes to
        /// define the output name of a Job function.</param>
        public BlobInputAttribute(string containerName)
        {
            ContainerName = containerName;
        }

        /// <summary>
        /// Gets or sets the name of the container to bind to. The path can contain tokens in curly
        /// braces to indicate a pattern to match. The matched name can be used in other binding attributes to
        /// define the output name of a Job function.
        /// </summary>
        public string ContainerName { get; set; }

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
