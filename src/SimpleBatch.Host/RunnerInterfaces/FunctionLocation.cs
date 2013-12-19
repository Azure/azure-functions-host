using System;

namespace Microsoft.WindowsAzure.Jobs
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
        public string AccountConnectionString { get; set; }

        // Uniquely stringize this object. Can be used for equality comparisons. 
        // $$$ Is this unique even for different derived types? 
        // $$$ This vs. ToString?
        public abstract string GetId();

        // Useful name for human display. This has no uniqueness properties and can't be used as a rowkey. 
        public abstract string GetShortName();

        // This is used for ICall. Convert from a short name to another FunctionLocation.
        // $$$ Reconcile methodName with GetShortName (which includes a type name)
        // Should x.ResolveFunctionLocation(x.GetShortName()).Equals(x) == true?
        // Any other invariants here?
        public virtual FunctionLocation ResolveFunctionLocation(string methodName)
        {
            throw new InvalidOperationException("Can't resolve function location for: " + methodName);
        }

        // ToString can be used as an azure row key.
        public override string ToString()
        {
            return TableClient.GetAsTableKey(this.GetId());
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
            return this.GetId().GetHashCode();
        }

        // Read a file from the function's location. 
        // $$$ Should this be byte[] instead? It's string because we expect caller will deserialize with JSON.
        public virtual string ReadFile(string filename)
        {
            return null; // not available.
        }
    }
}
