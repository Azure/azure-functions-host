using System;

namespace Microsoft.Azure.Jobs.Host.Indexers
{
    internal class FunctionIndexingException : Exception
    {
        public FunctionIndexingException(string methodName, Exception innerException)
            : base("Error indexing method '" + methodName + "'", innerException)
        {
        }
    }
}
