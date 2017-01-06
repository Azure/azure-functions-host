// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Host.Config;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Xunit;
using System.Threading.Tasks;
using System.Reflection;
using Microsoft.Azure.WebJobs.Host.Bindings;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Common
{
    public class BindingFactoryHelperTests
    {
        // Unit test that we can properly extract TMessage from a parameter type. 
        [Fact]
        public void GetCoreType()
        {
            Assert.Equal(null, BindingFactoryHelpers.GetAsyncCollectorCoreType(typeof(Widget))); // Not an AsyncCollector type

            Assert.Equal(typeof(Widget), BindingFactoryHelpers.GetAsyncCollectorCoreType(typeof(IAsyncCollector<Widget>)));
            Assert.Equal(typeof(Widget), BindingFactoryHelpers.GetAsyncCollectorCoreType(typeof(ICollector<Widget>)));
            Assert.Equal(typeof(Widget), BindingFactoryHelpers.GetAsyncCollectorCoreType(typeof(Widget).MakeByRefType()));
            Assert.Equal(typeof(Widget), BindingFactoryHelpers.GetAsyncCollectorCoreType(typeof(Widget[]).MakeByRefType()));

            // Verify that 'out' takes precedence over generic. 
            Assert.Equal(typeof(IFoo<Widget>), BindingFactoryHelpers.GetAsyncCollectorCoreType(typeof(IFoo<Widget>).MakeByRefType()));
        }

        // Random generic type to use in tests. 
        interface IFoo<T>
        {
        }

        class Widget { } 
    }
}
