using System;

namespace Microsoft.Azure.Jobs
{
    // Describes a function somewhere on the cloud
    // This serves as a primary key. 
    // $$$ generalize this. Don't necessarily need the account information 
    // This is static. Still needs args to get invoked. 
    // This can be serialized to JSON. 
    internal abstract class FunctionLocation
    {
        // The account this function is associated with. 
        // This will be used to resolve all static bindings.
        // This is used at runtime bindings. 
        public string StorageConnectionString { get; set; }

        public string ServiceBusConnectionString { get; set; }

        public string FullName { get; set; }

        // Uniquely stringize this object. Can be used for equality comparisons. 
        // $$$ Is this unique even for different derived types? 
        // $$$ This vs. ToString?
        public abstract string GetId();

        // Useful name for human display. This has no uniqueness properties and can't be used as a rowkey. 
        public abstract string GetShortName();

        // ToString can be used as an azure row key.
        public override string ToString()
        {
            return TableClient.GetAsTableKey(GetId());
        }

        public override bool Equals(object obj)
        {
            FunctionLocation other = obj as FunctionLocation;
            if (other == null)
            {
                return false;
            }

            return GetId() == other.GetId();
        }

        public override int GetHashCode()
        {
            return GetId().GetHashCode();
        }
    }
}
