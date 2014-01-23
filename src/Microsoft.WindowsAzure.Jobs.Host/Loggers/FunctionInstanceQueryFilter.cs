namespace Microsoft.WindowsAzure.Jobs
{
    // $$$ Filter to storage container? Function, Date? Success status? Becomes a full-fledged database!
    internal class FunctionInstanceQueryFilter
    {
        // Only return functions in the given account name
        public string AccountName { get; set; }

        public FunctionLocation Location { get; set; }

        // If has value, the filter whether function has completed and succeeded. 
        // Does not include Queued functions or Running functions. 
        // $$$ Change to FunctionInstanceStatus and include those?
        public bool? Succeeded;
    }
}
