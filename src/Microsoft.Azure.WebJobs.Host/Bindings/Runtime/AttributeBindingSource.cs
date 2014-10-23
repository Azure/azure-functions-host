// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Host.Bindings.Runtime
{
    internal class AttributeBindingSource : IAttributeBindingSource
    {
        private readonly IBindingProvider _bindingProvider;
        private readonly AmbientBindingContext _context;

        public AttributeBindingSource(IBindingProvider bindingProvider, AmbientBindingContext context)
        {
            if (bindingProvider == null)
            {
                throw new ArgumentNullException("bindingProvider");
            }

            if (context == null)
            {
                throw new ArgumentNullException("context");
            }

            _bindingProvider = bindingProvider;
            _context = context;
        }

        public AmbientBindingContext AmbientBindingContext
        {
            get { return _context; }
        }

        public Task<IBinding> BindAsync<TValue>(Attribute attribute, CancellationToken cancellationToken)
        {
            return _bindingProvider.TryCreateAsync(new BindingProviderContext(
                new FakeParameterInfo(typeof(TValue), attribute),
                bindingDataContract: null, cancellationToken: cancellationToken));
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
