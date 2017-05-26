// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using Microsoft.Azure.WebJobs.Host.Config;
using Microsoft.Azure.WebJobs.Description;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests
{
    public class ExtensionConfigContextTests
    {
        [Fact]
        public void BasicRules()
        {
            var config = new JobHostConfiguration();
            var ctx = new ExtensionConfigContext
            {
                Config = config
            };


            // Simulates extension initialization scope.
            {
                var binding = ctx.AddBindingRule<TestAttribute>();

                binding.WhenIsNull("Mode").
                        BindToInput<int>(attr => attr.Value);

                binding.WhenIsNotNull("Mode").
                        BindToInput<string>(attr => attr.ToString());

                StringWriter sw = new StringWriter();
                binding.DebugDumpGraph(sw);

                AssertStringEqual(
@"[TestAttribute] -->[filter: (Mode == null)]-->Int32
[TestAttribute] -->[filter: (Mode != null)]-->String", sw.ToString());
            }
        }

        static void AssertStringEqual(string expected, string actual)
        {
            var a = expected.Trim().Replace("\r", "");
            var b = actual.Trim().Replace("\r", "");

            Assert.Equal(a, b);
        }

        // Test register converters via the ExtensionConfigContext interfaces.
        [Fact]
        public void Converters()
        {
            var config = new JobHostConfiguration();
            var ctx = new ExtensionConfigContext
            {
                Config = config
            };


            // Simulates extension initialization scope.
            {
                ctx.AddBindingRule<TestAttribute>().AddConverter<int, string>(val => "specific"); // specific 
                ctx.AddConverter<int, string>(val => "general"); // general 
            }
            ctx.ApplyRules();

            var cm = config.ConverterManager;

            {
                var generalConverter = cm.GetConverter<int, string, Attribute>();
                var result = generalConverter(12, null, null);
                Assert.Equal("general", result);
            }

            {
                var specificConverter = cm.GetConverter<int, string, TestAttribute>();
                var result = specificConverter(12, null, null);
                Assert.Equal("specific", result);
            }
        }

        [Fact]
        public void Error_IfMissingBindingAttribute()
        {
            var config = new JobHostConfiguration();
            var ctx = new ExtensionConfigContext
            {
                Config = config
            };
                      
            // 'Attribute' 
            Assert.Throws<InvalidOperationException>(() => ctx.AddBindingRule<Attribute>());
        }

        [Fact]
        public void Error_CallingAddBindingRule_Multiple_Times()
        {
            var config = new JobHostConfiguration();
            var ctx = new ExtensionConfigContext
            {
                Config = config
            };

            // First time is fine
            ctx.AddBindingRule<TestAttribute>();

            // Second time on the same Attribute is an error. 
            Assert.Throws<InvalidOperationException>(() => ctx.AddBindingRule<TestAttribute>());
        }

        [Fact]
        public void ErrorOnDanglingWhen()
        {
            var config = new JobHostConfiguration();
            var ctx = new ExtensionConfigContext
            {
                Config = config
            };

            // Simulates extension initialization scope.
            {
                var binding = ctx.AddBindingRule<TestAttribute>();
                binding.WhenIsNull("Mode"); // Error! Dangling filter, should end in a Bind() call.
            }
            Assert.Throws<InvalidOperationException>(() => ctx.ApplyRules());
        }

        [Binding]
        public class TestAttribute : Attribute
        {
            public string Mode { get; set; }
            public int Value { get; set; }
        }
    }
}
