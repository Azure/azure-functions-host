namespace Microsoft.WindowsAzure.Jobs
{
    internal class IndexResults
    {
        public FunctionDefinition[] NewFunctions { get; set; }

        public FunctionDefinition[] UpdatedFunctions { get; set; }

        public FunctionDefinition[] DeletedFunctions { get; set; }

        public string[] Errors { get; set; }
    }
}
