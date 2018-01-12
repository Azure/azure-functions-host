// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.WebJobs.Description;
using Microsoft.Azure.WebJobs.Script.Binding;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class ExtensionBindingTests
    {
        [Fact]
        public void GetAttributeBuilderInfo_ReturnsExpectedAttribute()
        {
            var attribute = new TestAttribute("constructorSetAttributeValue")
            {
                BoolParameter = true,
                StringParameter = "stringParameterValue",
                IntParameter = 42,
                DoesNotAutoResolveParameter = "doesNotAutoResolveParameterValue"
            };
            var builderInfo = ExtensionBinding.GetAttributeBuilderInfo(attribute);

            TestAttribute result = (TestAttribute)builderInfo.Constructor.Invoke(builderInfo.ConstructorArgs);

            Assert.Equal(attribute.ConstructorSetParameter, result.ConstructorSetParameter);

            // 6 properties on the object, but AppSetting will be null, so 5 will be expected.
            Assert.Equal(5, builderInfo.Properties.Count);

            var properties = builderInfo.Properties.ToDictionary(p => p.Key.Name, p => p.Value);
            Assert.Throws(typeof(KeyNotFoundException), () => properties["AppSettingSetParameter"]);
            Assert.Equal(attribute.BoolParameter, (bool)properties["BoolParameter"]);
            Assert.Equal(attribute.ConstructorSetParameter, (string)properties["ConstructorSetParameter"]);
            Assert.Equal(attribute.DoesNotAutoResolveParameter, (string)properties["DoesNotAutoResolveParameter"]);
            Assert.Equal(attribute.IntParameter, (int)properties["IntParameter"]);
            Assert.Equal(attribute.StringParameter, (string)properties["StringParameter"]);
        }

        [AttributeUsage(AttributeTargets.Parameter | AttributeTargets.ReturnValue)]
        [Binding]
        public sealed class TestAttribute : Attribute
        {
            public TestAttribute(string constructorSetParameter)
            {
                ConstructorSetParameter = constructorSetParameter;
            }

            [AutoResolve]
            public int IntParameter { get; set; }

            [AutoResolve]
            public string StringParameter { get; set; }

            [AutoResolve]
            public string ConstructorSetParameter { get; set; }

            [AutoResolve]
            public bool BoolParameter { get; set; }

            [AppSetting]
            public string AppSettingSetParameter { get; set; }

            public string DoesNotAutoResolveParameter { get; set; }
        }
    }
}
