using System;
using System.Globalization;

namespace Microsoft.WindowsAzure.Jobs
{
    // Take in multiple inputs. Used for aggregation. 
    // [BlobInputs("container/{deployId}/{date}/{name}.csv"]
    [AttributeUsage(AttributeTargets.Parameter)]
    internal class BlobInputsAttribute : Attribute
    {
        public BlobInputsAttribute(string blobPathPattern)
        {
            BlobPathPattern = blobPathPattern;
        }

        public string BlobPathPattern { get; private set; }

        public override string ToString()
        {
            return String.Format(CultureInfo.InvariantCulture, "[BlobInputs({0})]", BlobPathPattern);
        }
    }
}
