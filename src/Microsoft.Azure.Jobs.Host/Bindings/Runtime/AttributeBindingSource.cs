using System;
using System.Reflection;
using System.Threading;

namespace Microsoft.Azure.Jobs.Host.Bindings.Runtime
{
    internal class AttributeBindingSource : IAttributeBindingSource
    {
        private readonly BindingContext _context;

        public AttributeBindingSource(BindingContext context)
        {
            _context = context;
        }

        public BindingContext BindingContext
        {
            get { return _context; }
        }

        public CancellationToken CancellationToken
        {
            get { return _context.CancellationToken; }
        }

        public IBinding Bind<TValue>(Attribute attribute)
        {
            return _context.BindingProvider.TryCreate(BindingProviderContext.Create(
                _context, new FakeParameterInfo(typeof(TValue), attribute), bindingDataContract: null));
        }

        // A non-reflection based implementation
        private class FakeParameterInfo : ParameterInfo
        {
            private readonly Attribute _attribute;

            public FakeParameterInfo(Type parameterType, Attribute attribute)
            {
                ClassImpl = parameterType;
                _attribute = attribute;
                AttrsImpl = ParameterAttributes.In;
                NameImpl = "?";
                MemberImpl = new FakeMemberInfo();
            }

            public override object[] GetCustomAttributes(Type attributeType, bool inherit)
            {
                if (attributeType == _attribute.GetType())
                {
                    return new Attribute[] { _attribute };
                }

                return null;
            }

            private class FakeMemberInfo : MemberInfo
            {
                public override Type DeclaringType
                {
                    get { throw new NotImplementedException(); }
                }

                public override object[] GetCustomAttributes(Type attributeType, bool inherit)
                {
                    throw new NotImplementedException();
                }

                public override object[] GetCustomAttributes(bool inherit)
                {
                    throw new NotImplementedException();
                }

                public override bool IsDefined(Type attributeType, bool inherit)
                {
                    throw new NotImplementedException();
                }

                public override MemberTypes MemberType
                {
                    get { return MemberTypes.Method; }
                }

                public override string Name
                {
                    get { throw new NotImplementedException(); }
                }

                public override Type ReflectedType
                {
                    get { throw new NotImplementedException(); }
                }
            }
        }
    }
}
