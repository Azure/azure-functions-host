// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Host.Config;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Xunit;
using System.Threading.Tasks;
using System.Reflection;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Common
{
    // Test BindingFactory's BindToGenericItem rule.
    public class BindToGenericItemTests
    {
        // Some custom type to bind to. 
        public class Widget
        {
            public static Widget New(TestAttribute attrResolved)
            {
                return new Widget { _value = attrResolved.Path };
            }

            public string _value;
        }

        public class TestAttribute : Attribute
        {
            public TestAttribute(string path)
            {
                this.Path = path;
            }

            [AutoResolve]
            public string Path { get; set; }
        }

        public class FakeExtClient : IExtensionConfigProvider
        {
            public void Initialize(ExtensionConfigContext context)
            {
                var bf = context.Config.BindingFactory;

                // Add [Test] support                
                var rule = bf.BindToGenericItem<TestAttribute>(Builder);
                context.RegisterBindingRules<TestAttribute>(rule);
            }

            private Task<object> Builder(TestAttribute attrResolved, Type parameterType)
            {
                var method = parameterType.GetMethod("New", BindingFlags.Static | BindingFlags.Public);
                var obj = method.Invoke(null, new object[] { attrResolved });
                return Task.FromResult(obj);
            }
        }

        public class Program
        {
            public string _value;

            public void Func([Test("abc-{k}")] Widget w)
            {
                _value = w._value;
            }
        }

        [Fact]
        public void Test()
        {
            var prog = new Program();
            var jobActivator = new FakeActivator();
            jobActivator.Add(prog);

            var host = TestHelpers.NewJobHost<Program>(jobActivator, new FakeExtClient());
            host.Call("Func", new { k = 1 });

            // Skipped first rule, applied second 
            Assert.Equal(prog._value, "abc-1");
        }
    }
}
