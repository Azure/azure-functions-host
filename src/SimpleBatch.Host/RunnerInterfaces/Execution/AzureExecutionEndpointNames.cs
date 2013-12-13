namespace RunnerInterfaces
{
    // Endpoint names for azure storage resources used for the service. 
    internal class AzureExecutionEndpointNames
    {
        // This is the container where the role can write console output logs for each run.
        // Useful to ensure this container has public access so that browsers can read the logs
        public const string ConsoleOuputLogContainerName = "daas-invoke-log";

        // When a function is completed executing, it can queue a message here. The queue reader can then
        // aggregate statistcs in a single threaded way. 
        public const string FunctionInvokeDoneQueue = "daas-invoke-done";
    }
}
