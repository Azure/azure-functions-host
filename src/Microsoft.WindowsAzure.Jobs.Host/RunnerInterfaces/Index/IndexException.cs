using System;
using System.Reflection;
using System.Text;

namespace Microsoft.WindowsAzure.Jobs
{
    internal class IndexException : Exception
    {
        public IndexException(string msg)
            : base(msg)
        {
        }

        public IndexException(string msg, Exception inner)
            : base(msg, inner)
        {
        }

        public IIndexLocation Location { get; set; }


        public static IndexException NewParameter(ParameterInfo parameter, Exception inner)
        {
            string msg = string.Format("Index error at parameter {0} on method {1}: {2}", parameter.Name, parameter.Member.Name, inner.Message);
            throw new IndexException(msg, inner);
        }

        public static IndexException NewMethod(string methodName, Exception inner)
        {
            string msg = string.Format("Index error method {0}: {1}", methodName, inner.Message);
            throw new IndexException(msg, inner);
        }

    }

    internal interface IIndexLocation
    {
        string ElementType { get; } // Parameter, Method, etc
        string Name { get; }
        IIndexLocation Parent { get; }
    }

    internal class IndexLocation : IIndexLocation
    {
        public string ElementType { get; set;  }
        public string Name { get; set; }
        public IIndexLocation Parent { get; set; }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            IIndexLocation x = this;
            bool first = true;
            while (x != null)
            {
                if (!first)
                {
                    sb.AppendFormat(" at ");
                }
                first = false;
                sb.AppendFormat("{0} {1}", ElementType, Name);                
            }
            return sb.ToString();
        }
    }

    
}
