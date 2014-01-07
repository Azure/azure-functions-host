namespace Microsoft.WindowsAzure.Jobs
{
    // Argument can implement this to be self describing. 
    // This can be queried on another thread. 
    internal interface ISelfWatch
    {
        // $$$ Expected that string is a single line. 
        // Use "; " to denote multiple lines. 
        string GetStatus();
    }
}
