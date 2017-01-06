// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Host.Config;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Common
{
    // Test for binding validator 
    public class BindingProviderFilterTests
    {
        public class TestAttribute : Attribute
        {
            public TestAttribute(string path)
            {
                this.Path = path;
            }

            [AutoResolve]
            public string Path { get; set; }
        }

        class Program
        {
            public string _value;
            public void Func([Test("%x%")] string x)
            {
                _value = x;
            }
        }

        // Fitler that throws a validation error. 
        [Fact]
        public void TestValidationError()
        {
            var nr = new FakeNameResolver().Add("x", "error");
            var host = TestHelpers.NewJobHost<Program>(nr, new FakeExtClient());

            TestHelpers.AssertIndexingError(() => host.Call("Func"), "Program.Func", FakeExtClient.IndexErrorMsg);
        }

        // Fitler that skips the first rule and lands on the second rule. 
        [Fact]
        public void TestSkip()
        {
            var prog = new Program();
            var jobActivator = new FakeActivator();
            jobActivator.Add(prog);

            var nr = new FakeNameResolver().Add("x", "false");
            var host = TestHelpers.NewJobHost<Program>(nr, jobActivator, new FakeExtClient());
            host.Call("Func");

            // Skipped first rule, applied second 
            Assert.Equal(prog._value, "xxx");
        }
        
        public class FakeExtClient : IExtensionConfigProvider
        {
            public void Initialize(ExtensionConfigContext context)
            {
                var bf = context.Config.BindingFactory;

                // Add [Test] support                
                var rule = bf.BindToInput<TestAttribute, string>(typeof(Converter1));                     
                var ruleValidate = bf.AddFilter<TestAttribute>(Filter, rule);
                var rule2 = bf.BindToInput<TestAttribute, string>(typeof(Converter2));
                context.RegisterBindingRules<TestAttribute>(ruleValidate, rule2);
            }

            class Converter1 : IConverter<TestAttribute, string>
            {
                public string Convert(TestAttribute attr)
                {
                    return attr.Path;
                }
            }
            class Converter2 : IConverter<TestAttribute, string>
            {
                public string Convert(TestAttribute attr)
                {
                    return "xxx";
                }
            }

            public const string IndexErrorMsg = "error 12345";

            private static bool Filter(TestAttribute attribute, Type parameterType)
            {
                Assert.Equal(typeof(string), parameterType); 

                // Validation example
                if (attribute.Path == "error")
                {
                    throw new InvalidOperationException(IndexErrorMsg);
                }
                if (attribute.Path == "false")
                {
                    return false;
                }
                return true;
            }
        }
    }
}