using System;
using System.Reflection;

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

        public static IndexException NewParameter(ParameterInfo parameter, Exception inner)
        {
            string msg = string.Format("Index error at parameter '{0}' on method '{1}': {2}", parameter.Name, parameter.Member.Name, inner.Message);
            throw new IndexException(msg, inner);
        }

        public static IndexException NewMethod(string methodName, Exception inner)
        {
            string msg = string.Format("Index error on method '{0}': {1}", methodName, inner.Message);
            throw new IndexException(msg, inner);
        }
    }
}
