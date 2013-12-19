using System;
using System.Reflection;

namespace Microsoft.WindowsAzure.Jobs
{
    // Take in multiple inputs. Used for aggregation. 
    // [BlobInputs("container\{deployId}\{date}\{name}.csv"]
    [AttributeUsage(AttributeTargets.Parameter)]
    public class BlobInputsAttribute : Attribute
    {
        public string BlobPathPattern { get; set; }

        public BlobInputsAttribute(string blobPathPattern)
        {
            this.BlobPathPattern = blobPathPattern;
        }

        public static BlobInputsAttribute Build(CustomAttributeData attr)
        {
            if (attr.Constructor.DeclaringType.FullName != typeof(BlobInputsAttribute).FullName)
            {
                return null;
            }

            string arg = (string)attr.ConstructorArguments[0].Value;
            return new BlobInputsAttribute(arg);
        }

        public override string ToString()
        {
            return string.Format("[BlobInputs({0})]", BlobPathPattern);
        }
    }
}
