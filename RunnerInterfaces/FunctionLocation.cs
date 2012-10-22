using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;

namespace RunnerInterfaces
{
    // Describes a function somewhere on the cloud
    // This serves as a primary key. 
    // $$$ generalize this. Don't necessarily need the account information 
    // This is static. Still needs args to get invoked. 
    // This can be serialized to JSON. 
    public class FunctionLocation
    {
        public CloudBlobDescriptor Blob { get; set; }

        // Entry point into the function 
        // $$$ If this is null, do we just CreateProcess on the executable directly?
        // - but then how would we bind parameters?
        public string TypeName { get; set; }
        public string MethodName { get; set; }

        public string GetId()
        {
            return string.Format(@"{0}\{1}\{2}", Blob.GetId(), TypeName, MethodName);
        }

        public static bool TryParse(string input, out FunctionLocation value)
        {
            throw new NotImplementedException();
        }

        // ToString can be used as an azure row key.
        public override string ToString()
        {
            return this.GetId().Replace('\\', '.');
        }

        public override bool Equals(object obj)
        {
            FunctionLocation other = obj as FunctionLocation;
            if (other == null)
            {
                return false;
            }

            return this.GetId() == other.GetId();
        }

        public override int GetHashCode()
        {
            return MethodName.GetHashCode();
        }
    }
}