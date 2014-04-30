using System;
using System.Reflection;

namespace Microsoft.Azure.Jobs
{
    // Bindings use ParameterInfo for a (Type, Name, IsOut) pair. 
    // provide a non-reflection based implementation. 
    // Reuse ParameterInfo rather than creating another abstraction. 
    internal class FakeParameterInfo : ParameterInfo
    {
        private readonly string _name;
        private readonly Type _type;
        private readonly ParameterAttributes _flags;
        private readonly object[] _attributes;

        public FakeParameterInfo(Type paramType, string name, bool isOut, object[] attributes = null)
        {
            _attributes = attributes ?? new object[0];
            _type = paramType;
            _name = name;

            // Both ref and out keywords are T&.
            // but only out keyword gives ParameterInfo.IsOut = true.
            _flags = isOut ? ParameterAttributes.Out : ParameterAttributes.In;
        }

        public override object[] GetCustomAttributes(bool inherit)
        {
            return _attributes;
        }

        public override ParameterAttributes Attributes
        {
            get
            {
                return _flags;
            }
        }

        public override Type ParameterType
        {
            get
            {
                return _type;
            }
        }

        public override string Name
        {
            get
            {
                if (_name == null)
                {
                    throw new InvalidOperationException("No parameter name is available");
                }
                return _name;
            }
        }
    }
}
