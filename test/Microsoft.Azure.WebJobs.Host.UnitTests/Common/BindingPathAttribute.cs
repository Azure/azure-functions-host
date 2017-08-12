// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Description;
using Microsoft.Azure.WebJobs.Host.Config;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Common
{
    // Sample attribute for binding to the {sys.methodname}
    [Binding]
    public class BindingPathAttribute : Attribute
    {
        [AutoResolve(Default = "{sys.methodname}")]
        public string Path { get; set; }


        public class Extension : IExtensionConfigProvider
        {
            public void Initialize(ExtensionConfigContext context)
            {
                var rule = context.AddBindingRule<BindingPathAttribute>();
                rule.BindToInput<string>(attr => attr.Path);
            }
        }
    }
}