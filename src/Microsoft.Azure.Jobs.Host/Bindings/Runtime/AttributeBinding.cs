using System;
using System.Reflection;
using System.Threading;

namespace Microsoft.Azure.Jobs.Host.Bindings.Runtime
{
    internal class AttributeBinding : IAttributeBinding
    {
        private readonly BindingContext _context;

        public AttributeBinding(BindingContext context)
        {
            _context = context;
        }

        public CancellationToken CancellationToken
        {
            get { return _context.CancellationToken; }
        }

        public IValueProvider Bind<TValue>(Attribute attribute)
        {
            IBinding binding = _context.BindingProvider.TryCreate(BindingProviderContext.Create(
                _context, new FakeParameterInfo(typeof(TValue), attribute), bindingDataContract: null));

            if (binding == null)
            {
                throw new InvalidOperationException("No binding found for attribute '" + attribute.GetType() + "'.");
            }

            return binding.Bind(_context);
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
