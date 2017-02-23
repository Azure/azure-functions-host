// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Bindings.Path;

namespace Microsoft.Azure.WebJobs.Host.UnitTests
{
    // "Fake Queue" support for 100% in-memory for unit test bindings. 
    // Put on a parameter to mark that it goes to a "FakeQueue". 
    public class FakeQueueAttribute : Attribute, IAttributeInvokeDescriptor<FakeQueueAttribute>
    {
        public FakeQueueAttribute() : this(null)
        {
        }

        public FakeQueueAttribute(string constructorCustomPolicy)
        {
            ConstructorCustomPolicy = constructorCustomPolicy;
        }

        [AutoResolve]
        public string Prefix { get; set; }

        [AutoResolve(ResolutionPolicyType = typeof(CustomResolutionPolicy))]
        public string CustomPolicy { get; set; }

        [AutoResolve(ResolutionPolicyType = typeof(CustomResolutionPolicy))]
        public string ConstructorCustomPolicy { get; private set; }

        internal string State1 { get; set; }

        internal string State2 { get; set; }

        public string ToInvokeString()
        {
            return this.Prefix;
        }
        public FakeQueueAttribute FromInvokeString(string invokeString)
        {
            return new FakeQueueAttribute("customPolicy") { Prefix = invokeString };
        }
        private class CustomResolutionPolicy : IResolutionPolicy
        {
            public string TemplateBind(PropertyInfo propInfo, Attribute attribute, BindingTemplate template, IReadOnlyDictionary<string, object> bindingData)
            {
                FakeQueueAttribute queueAttribute = (FakeQueueAttribute)attribute;

                if (propInfo.Name == nameof(CustomPolicy))
                {
                    queueAttribute.State1 += "value1";
                }

                if (propInfo.Name == nameof(ConstructorCustomPolicy))
                {
                    queueAttribute.State2 += "value2";
                }

                return template.Bind(bindingData);
            }
        }
    }
}