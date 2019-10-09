// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Dynamic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.WebJobs.Script.Tests;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public interface ITestInterface
    {
    }

    public struct TestStruct
    {
        public string Name { get; set; }

        public string Location { get; set; }
    }

    public class TestPoco
    {
        public string Name { get; set; }

        public string Location { get; set; }
    }

    public class TestPocoEx : TestPoco
    {
        public int Age { get; set; }

        public string Phone { get; set; }

        public string Readonly { get; }

        public IDictionary<string, string> Properties { get; set; }
    }

    public class UtilityTests
    {
        [Fact]
        public async Task InvokeWithRetriesAsync_Throws_WhenRetryCountExceeded()
        {
            var ex = new Exception("Kaboom!");
            var result = await Assert.ThrowsAsync<Exception>(async () =>
            {
                await Utility.InvokeWithRetriesAsync(() => throw ex, maxRetries: 4, retryInterval: TimeSpan.FromMilliseconds(25));
            });
            Assert.Same(ex, result);
        }

        [Fact]
        public async Task InvokeWithRetriesAsync_Succeeds_AfterSeveralRetries()
        {
            int count = 0;
            await Utility.InvokeWithRetriesAsync(() =>
            {
                if (count++ < 3)
                {
                    throw new Exception("Kaboom!");
                }
            }, maxRetries: 4, retryInterval: TimeSpan.FromMilliseconds(25));
        }

        [Fact]
        public async Task DelayWithBackoffAsync_Returns_WhenCancelled()
        {
            var tokenSource = new CancellationTokenSource();
            tokenSource.CancelAfter(500);

            // set up a long delay and ensure it is cancelled
            Stopwatch sw = new Stopwatch();
            sw.Start();
            await Utility.DelayWithBackoffAsync(20, tokenSource.Token);
            sw.Stop();
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2), $"Expected sw.Elapsed < TimeSpan.FromSeconds(2); Actual: {sw.Elapsed.TotalMilliseconds}");
        }

        [Fact]
        public async Task DelayWithBackoffAsync_DelaysAsExpected()
        {
            Stopwatch sw = new Stopwatch();
            sw.Start();
            await Utility.DelayWithBackoffAsync(2, CancellationToken.None);
            sw.Stop();

            // Workaround annoying test failures such as "Expected sw.Elapsed >= TimeSpan.FromSeconds(2); Actual: 1999.4092" by waiting slightly less than 2 seconds
            // Not sure what causes it, but either Task.Delay sometimes doesn't wait quite long enough or there is some clock skew.
            TimeSpan roundedElapsedSpan = sw.Elapsed.RoundSeconds(digits: 1);
            Assert.True(roundedElapsedSpan >= TimeSpan.FromSeconds(2), $"Expected roundedElapsedSpan >= TimeSpan.FromSeconds(2); Actual: {roundedElapsedSpan.TotalSeconds}");
        }

        [Theory]
        [InlineData(1, null, null, null, "00:00:00")]
        [InlineData(2, null, null, null, "00:00:02")]
        [InlineData(3, null, null, null, "00:00:04")]
        [InlineData(4, null, null, null, "00:00:08")]
        [InlineData(5, null, null, null, "00:00:016")]
        [InlineData(6, null, null, null, "00:00:32")]
        [InlineData(6, null, null, "00:00:20", "00:00:20")] // test min/max
        [InlineData(2, null, "00:00:10", null, "00:00:10")]
        [InlineData(6, null, "00:00:10", "00:00:20", "00:00:20")]
        [InlineData(2, null, "00:00:10", "00:00:20", "00:00:10")]
        [InlineData(1, "00:00:00.100", null, null, "00:00:00.000")] // changing the base unit
        [InlineData(2, "00:00:00.100", null, null, "00:00:00.200")]
        [InlineData(3, "00:00:00.100", null, null, "00:00:00.400")]
        [InlineData(4, "00:00:00.100", null, null, "00:00:00.800")]
        [InlineData(5, "00:00:00.100", null, null, "00:00:01.600")]
        [InlineData(6, "00:00:00.100", null, null, "00:00:03.200")]
        public void ComputeBackoff_ReturnsExpectedValue(int exponent, string unitValue, string minValue, string maxValue, string expected)
        {
            TimeSpan? unit = null;
            if (!string.IsNullOrEmpty(unitValue))
            {
                unit = TimeSpan.Parse(unitValue);
            }
            TimeSpan? min = null;
            if (!string.IsNullOrEmpty(minValue))
            {
                min = TimeSpan.Parse(minValue);
            }
            TimeSpan? max = null;
            if (!string.IsNullOrEmpty(maxValue))
            {
                max = TimeSpan.Parse(maxValue);
            }

            TimeSpan result = Utility.ComputeBackoff(exponent, unit, min, max);
            TimeSpan expectedTimespan = TimeSpan.Parse(expected);
            Assert.Equal(expectedTimespan, result);
        }

        [Theory]
        [InlineData(typeof(TestPoco), true)]
        [InlineData(typeof(TestStruct), true)]
        [InlineData(typeof(ITestInterface), false)]
        [InlineData(typeof(Guid), false)]
        [InlineData(typeof(int), false)]
        [InlineData(typeof(string), false)]
        public void IsValidUserType_ReturnsExpectedValue(Type type, bool expected)
        {
            Assert.Equal(expected, Utility.IsValidUserType(type));
        }

        [Theory]
        [InlineData("FooBar", "fooBar")]
        [InlineData("FOOBAR", "fOOBAR")]
        [InlineData("fooBar", "fooBar")]
        [InlineData("foo", "foo")]
        [InlineData("Foo", "foo")]
        [InlineData("FOO", "fOO")]
        [InlineData("f", "f")]
        [InlineData("F", "f")]
        [InlineData(null, null)]
        [InlineData("", "")]
        public void ToLowerFirstCharacter_ReturnsExpectedResult(string input, string expected)
        {
            Assert.Equal(Utility.ToLowerFirstCharacter(input), expected);
        }

        [Fact]
        public void ApplyBindingData_HandlesNestedJsonPayloads()
        {
            string input = "{ 'test': 'testing', 'baz': 123, 'subObject': { 'p1': 777, 'p2': 888 }, 'subArray': [ { 'subObject': 'foobar' } ] }";

            var bindingData = new Dictionary<string, object>
            {
                { "foo", "Value1" },
                { "bar", "Value2" },
                { "baz", "Value3" }
            };

            Utility.ApplyBindingData(input, bindingData);

            Assert.Equal(5, bindingData.Count);
            Assert.Equal("Value1", bindingData["foo"]);
            Assert.Equal("Value2", bindingData["bar"]);
            Assert.Equal("testing", bindingData["test"]);

            JObject subObject = (JObject)bindingData["subObject"];
            Assert.Equal(888, (int)subObject["p2"]);

            // input data overrides ambient data
            Assert.Equal("123", bindingData["baz"]);
        }

        [Fact]
        public void FlattenException_AggregateException_ReturnsExpectedResult()
        {
            ApplicationException ex1 = new ApplicationException("Incorrectly configured setting 'Foo'");
            ex1.Source = "Acme.CloudSystem";

            // a dupe of the first
            ApplicationException ex2 = new ApplicationException("Incorrectly configured setting 'Foo'");
            ex1.Source = "Acme.CloudSystem";

            AggregateException aex = new AggregateException("One or more errors occurred.", ex1, ex2);

            string formattedResult = Utility.FlattenException(aex);
            Assert.Equal("Acme.CloudSystem: Incorrectly configured setting 'Foo'.", formattedResult);
        }

        [Fact]
        public void FlattenException_SingleException_ReturnsExpectedResult()
        {
            ApplicationException ex = new ApplicationException("Incorrectly configured setting 'Foo'");
            ex.Source = "Acme.CloudSystem";

            string formattedResult = Utility.FlattenException(ex);
            Assert.Equal("Acme.CloudSystem: Incorrectly configured setting 'Foo'.", formattedResult);
        }

        [Fact]
        public void FlattenException_MultipleInnerExceptions_ReturnsExpectedResult()
        {
            ApplicationException ex1 = new ApplicationException("Exception message 1");
            ex1.Source = "Source1";

            ApplicationException ex2 = new ApplicationException("Exception message 2.", ex1);
            ex2.Source = "Source2";

            ApplicationException ex3 = new ApplicationException("Exception message 3", ex2);

            string formattedResult = Utility.FlattenException(ex3);
            Assert.Equal("Exception message 3. Source2: Exception message 2. Source1: Exception message 1.", formattedResult);
        }

        [Fact]
        public void RemoveUTF8ByteOrderMark_RemovesBOM()
        {
            string bom = Encoding.UTF8.GetString(Encoding.UTF8.GetPreamble());
            var inputString = "TestString";
            var testInput = bom + inputString;

            string result = Utility.RemoveUtf8ByteOrderMark(testInput);

            Assert.Equal(inputString.Length + bom.Length, testInput.Length);
            Assert.Equal(inputString.Length, result.Length);
            Assert.Equal(inputString, result);
        }

        [Fact]
        public void RemoveUTF8ByteOrderMark_WithNoBOM_ReturnsOriginalString()
        {
            var inputString = "TestString";
            string result = Utility.RemoveUtf8ByteOrderMark(inputString);

            Assert.Equal(inputString.Length, result.Length);
            Assert.Equal(inputString, result);
        }

        [Fact]
        public void ToJObject_ReturnsExpectedResult()
        {
            dynamic child = new ExpandoObject();
            child.Name = "Mary";
            child.Location = "Seattle";
            child.Age = 5;

            dynamic parent = new ExpandoObject();
            parent.Name = "Bob";
            parent.Location = "Seattle";
            parent.Age = 40;
            parent.Children = new object[] { child };

            JObject resultParent = Utility.ToJObject(parent);

            Assert.Equal(resultParent["Name"], parent.Name);
            Assert.Equal(resultParent["Location"], parent.Location);
            Assert.Equal(resultParent["Age"], parent.Age);

            var children = (JArray)resultParent["Children"];
            Assert.Equal(1, children.Count);
            var resultChild = (JObject)children[0];
            Assert.Equal(resultChild["Name"], child.Name);
            Assert.Equal(resultChild["Location"], child.Location);
            Assert.Equal(resultChild["Age"], child.Age);
        }

        [Fact]
        public void ToJson_StripsFunctions_FromExpandoObjects()
        {
            // {
            //    func: () => { },
            //    nested:
            //            {
            //                func: () => { }
            //    },
            //    array: [
            //        { func: () => { } }
            //    ],
            //    value: "value"
            // };

            Action f = () => { };
            dynamic val = new ExpandoObject();
            val.func = f;
            val.nested = new ExpandoObject() as dynamic;
            val.nested.func = f;
            dynamic arrExpando = new ExpandoObject();
            arrExpando.func = f;
            val.array = new ExpandoObject[1] { arrExpando as ExpandoObject };
            val.value = "value";

            var json = Utility.ToJson(val as ExpandoObject, Newtonsoft.Json.Formatting.None);
            Assert.Equal("{\"nested\":{},\"array\":[{}],\"value\":\"value\"}", json);
        }

        [Theory]
        [InlineData(typeof(ExpandoObject), false)]
        [InlineData(typeof(string), false)]
        [InlineData(typeof(int), false)]
        [InlineData(typeof(int?), true)]
        public void IsNullable_ReturnsExpectedResult(Type type, bool expected)
        {
            Assert.Equal(expected, Utility.IsNullable(type));
        }

        [Theory]
        [InlineData("", null, false)]
        [InlineData(null, null, false)]
        [InlineData("http://storage.blob.core.windows.net/functions/func.zip?sr=c&si=policy&sig=f%2BGLvBih%2BoFuQvckBSHWKMXwqGJHlPkESmZh9pjnHuc%3D",
            "http://storage.blob.core.windows.net/functions/func.zip...", true)]
        [InlineData("http://storage.blob.core.windows.net/functions/func.zip",
            "http://storage.blob.core.windows.net/functions/func.zip", true)]
        [InlineData("https://storage.blob.core.windows.net/functions/func.zip",
            "https://storage.blob.core.windows.net/functions/func.zip", true)]
        [InlineData("https://storage.blob.core.windows.net/functions/func.zip?",
            "https://storage.blob.core.windows.net/functions/func.zip...", true)]
        public void CleanUrlTests(string url, string expectedCleanUrl, bool cleanResult)
        {
            string cleanedUrl;
            Assert.Equal(cleanResult, Utility.TryCleanUrl(url, out cleanedUrl));
            Assert.Equal(expectedCleanUrl, cleanedUrl);
        }

        [Theory]
        [InlineData("httpTrigger", true)]
        [InlineData("manualTrigger", true)]
        [InlineData("HttptRIGGER", true)]
        [InlineData("MANUALtRIGGER", true)]
        [InlineData(null, false)]
        [InlineData("", false)]
        [InlineData("blob", false)]
        public void IsHttporManualTriggerTests(string triggerType, bool expectedResult)
        {
            Assert.Equal(expectedResult, Utility.IsHttporManualTrigger(triggerType));
        }

        [Theory]
        [InlineData("createIsolationEnvironment", "tr-TR", true)]
        [InlineData("HttpTrigger2", "en-US", true)]
        [InlineData("HttptRIGGER", "ja-JP", true)]
        [InlineData("Function-200", "ja-JP", true)]
        [InlineData("MaNUALtRIGGER", "es-ES", true)]
        [InlineData("MáNUALtRIGGER", "es-ES", false)]
        [InlineData("hello!", "en-US", false)]
        [InlineData("コード", "ja-JP", false)]
        [InlineData("🙅", "en-US", false)]
        public void IsValidFunctionNameTests(string functionName, string cultureInfo, bool expectedResult)
        {
            CultureInfo defaultCulture = Thread.CurrentThread.CurrentCulture;
            Thread.CurrentThread.CurrentCulture = new CultureInfo(cultureInfo);
            Assert.Equal(expectedResult, Utility.IsValidFunctionName(functionName));
            Thread.CurrentThread.CurrentCulture = defaultCulture;
        }

        [Theory]
        [InlineData("node", "node")]
        [InlineData("java", "java")]
        [InlineData("", "node")]
        [InlineData(null, "java")]
        public void IsSupported_Returns_True(string language, string funcMetadataLanguage)
        {
            FunctionMetadata func1 = new FunctionMetadata()
            {
                Name = "func1",
                Language = funcMetadataLanguage
            };
            Assert.True(Utility.IsFunctionMetadataLanguageSupportedByWorkerRuntime(func1, language));
        }

        [Theory]
        [InlineData("node", "java")]
        [InlineData("java", "node")]
        [InlineData("python", "")]
        public void IsSupported_Returns_False(string language, string funcMetadataLanguage)
        {
            FunctionMetadata func1 = new FunctionMetadata()
            {
                Name = "func1",
                Language = funcMetadataLanguage
            };
            Assert.False(Utility.IsFunctionMetadataLanguageSupportedByWorkerRuntime(func1, language));
        }
    }
}
