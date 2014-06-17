using System;

namespace Microsoft.Azure.Jobs
{
    internal class IndexException : Exception
    {
        public IndexException(string message)
            : base(message)
        {
        }

        public IndexException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        public static IndexException NewMethod(string methodName, Exception inner)
        {
            string msg = string.Format("Index error on method '{0}': {1}", methodName, inner.Message);
            throw new IndexException(msg, inner);
        }
    }
}
