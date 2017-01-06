// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Host.Config;
using Microsoft.Azure.WebJobs.Host.Indexers;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Common
{
    public class ValidationTests
    {
        public class TestAttribute : Attribute
        {
            public bool Bad { get; set; }

            internal const string ErrorMessage = "Test attribute is not valid!";

            [AutoResolve]
            public string Path { get; set; }


            public void ValidateAtIndexTime(Type parameterType)
            {
                // Verify that FX is passing the expected paramteer type.
                Assert.True(parameterType == typeof(Widget) || parameterType == typeof(Widget2));

                // Test that validation is valled after INameResolver has handled %%, but before {} are resolved. 
                Assert.Equal("v1-{k2}", this.Path);

                if (this.Bad)
                {
                    throw new InvalidOperationException(TestAttribute.ErrorMessage);
                }
            }
        }

        // Some arbitrary types to use in parameters.  
        public class Widget { }

        // Some arbitrary type. 
        public class Widget2 { }

        public class FunctionBase
        {
            [NoAutomaticTrigger]
            public void Valid() { }
        }

        public class BadFunction : FunctionBase
        {
            public void Bad([Test(Bad = true, Path = "%k1%-{k2}")] Widget x)
            {
            }        
        }

        public class GoodFunction : FunctionBase
        {
            public void Good([Test(Bad= false, Path = "%k1%-{k2}")] Widget x)
            {
            }
        }

        public class FakeExtClient : IExtensionConfigProvider, IConverter<TestAttribute, Widget>
        {
            public void Initialize(ExtensionConfigContext context)
            {
                var bf = context.Config.BindingFactory;

                // Add [Test] support                
                var rule = bf.BindToInput<TestAttribute, Widget>(this);
                context.RegisterBindingRules<TestAttribute>(ValidateAtIndexTime, rule);
            }

            public Widget Convert(TestAttribute attr)
            {
                return new Widget();
            }

            private static void ValidateAtIndexTime(TestAttribute attribute, Type parameterType)
            {
                attribute.ValidateAtIndexTime(parameterType);
            }
        }

        [Fact]
        public void TestValidatorFails()
        {
            var nr = new FakeNameResolver().Add("k1", "v1");
            var host = TestHelpers.NewJobHost<BadFunction>(new FakeExtClient(), nr);

            TestHelpers.AssertIndexingError(
                () => host.Call("Valid"),
                "BadFunction.Bad", TestAttribute.ErrorMessage);
        }

        [Fact]
        public void TestValidatorSucceeds()
        {
            var nr = new FakeNameResolver().Add("k1", "v1");
            var host = TestHelpers.NewJobHost<GoodFunction>(new FakeExtClient(), nr);
            host.Call("Good", new { k2 = "xxxx" } ); 
        }

        // Register [Test]  with 2 rules and a local validator. 
        public class FakeExtClient2 : IExtensionConfigProvider,
            IConverter<TestAttribute, Widget>,
            IConverter<TestAttribute, Widget2>
        {
            public void Initialize(ExtensionConfigContext context)
            {
                var bf = context.Config.BindingFactory;

                // Add [Test] support
                var rule1 = bf.BindToInput<TestAttribute, Widget>(this);
                var rule2 = bf.BindToInput<TestAttribute, Widget2>(this);
                var rule2Validator = bf.AddValidator<TestAttribute>(LocalValidator, rule2);

                context.RegisterBindingRules<TestAttribute>(rule2Validator, rule1);
            }

            Widget IConverter<TestAttribute, Widget>.Convert(TestAttribute attr)
            {
                return new Widget();
            }

            Widget2 IConverter<TestAttribute, Widget2>.Convert(TestAttribute attr)
            {
                return new Widget2();
            }

            private void LocalValidator(TestAttribute attribute, Type parameterType)
            {
                attribute.ValidateAtIndexTime(parameterType);
            }
        }

        public class LocalFunction1 : FunctionBase
        {
            // No validation on Widget-rule to cathc that ths is bad. 
            // So this passes. Validator is never run. 
            public void NoValidation([Test(Bad = true, Path = "%k1%-{k2}")] Widget x)
            {
            }
        }

        public class LocalFunction2 : FunctionBase
        {
            // Validation rule does run on this parameter, catches the failure. 
            public void WithValidation([Test(Bad = true, Path = "%k1%-{k2}")] Widget2 x)
            {
            }
        }

        [Fact]
        public void TestLocalValidatorSkipped()
        {
            // Local validator only run if we use the given rule. 
            var nr = new FakeNameResolver().Add("k1", "v1");
            var host = TestHelpers.NewJobHost<LocalFunction1>(new FakeExtClient2(), nr);

            host.Call("NoValidation", new { k2 = "xxxx" }); // Succeeds since validate doesn't run on this rule             
        }

        [Fact]
        public void TestLocalValidatorApplied()
        {
            // Local validator only run if we use the given rule. 
            var nr = new FakeNameResolver().Add("k1", "v1");
            var host = TestHelpers.NewJobHost<LocalFunction2>(new FakeExtClient2(), nr);

            TestHelpers.AssertIndexingError(
                () => host.Call("WithValidation"),
                "LocalFunction2.WithValidation", TestAttribute.ErrorMessage);
            
        }
    }
}
