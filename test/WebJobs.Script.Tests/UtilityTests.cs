// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.ExtensionBundle;
using Microsoft.Azure.WebJobs.Script.Models;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Newtonsoft.Json.Linq;
using NuGet.Versioning;
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

    public class TestClass : ITestInterface
    {
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
        private TestLogger _testLogger = new TestLogger("test");

        [Fact]
        public void TryGetHostService_ReturnsExpectedResult()
        {
            ITestInterface test = new TestClass();
            var scriptHostManagerMock = new Mock<IScriptHostManager>(MockBehavior.Strict);
            var serviceProviderMock = scriptHostManagerMock.As<IServiceProvider>();
            serviceProviderMock.Setup(p => p.GetService(typeof(ITestInterface))).Returns(() => test);

            Assert.True(Utility.TryGetHostService(scriptHostManagerMock.Object, out ITestInterface result));
            Assert.Same(test, result);

            test = null;
            Assert.False(Utility.TryGetHostService(scriptHostManagerMock.Object, out result));
            Assert.Null(result);
        }

        [Fact]
        public void TryGetHostService_ObjectDisposed_ReturnsFalse()
        {
            var scriptHostManagerMock = new Mock<IScriptHostManager>(MockBehavior.Strict);
            var serviceProviderMock = scriptHostManagerMock.As<IServiceProvider>();
            serviceProviderMock.Setup(p => p.GetService(typeof(ITestInterface))).Throws(new ObjectDisposedException("Test"));

            Assert.False(Utility.TryGetHostService(scriptHostManagerMock.Object, out ITestInterface result));
            Assert.Null(result);
        }

        [Fact]
        public void TryGetHostService_NotServiceProvider_ReturnsFalse()
        {
            var scriptHostManagerMock = new Mock<IScriptHostManager>(MockBehavior.Strict);

            Assert.False(Utility.TryGetHostService(scriptHostManagerMock.Object, out ITestInterface result));
            Assert.Null(result);
        }

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
            var sw = ValueStopwatch.StartNew();
            await Utility.DelayWithBackoffAsync(20, tokenSource.Token);
            var elapsed = sw.GetElapsedTime();
            Assert.True(elapsed < TimeSpan.FromSeconds(2), $"Expected sw.Elapsed < TimeSpan.FromSeconds(2); Actual: {elapsed.TotalMilliseconds}");
        }

        [Fact]
        public async Task DelayWithBackoffAsync_DelaysAsExpected()
        {
            var sw = ValueStopwatch.StartNew();
            await Utility.DelayWithBackoffAsync(2, CancellationToken.None);
            var elapsed = sw.GetElapsedTime();

            // Workaround annoying test failures such as "Expected sw.Elapsed >= TimeSpan.FromSeconds(2); Actual: 1999.4092" by waiting slightly less than 2 seconds
            // Not sure what causes it, but either Task.Delay sometimes doesn't wait quite long enough or there is some clock skew.
            TimeSpan roundedElapsedSpan = elapsed.RoundSeconds(digits: 1);
            Assert.True(roundedElapsedSpan >= TimeSpan.FromSeconds(2), $"Expected roundedElapsedSpan >= TimeSpan.FromSeconds(2); Actual: {roundedElapsedSpan.TotalSeconds}");
        }

        [Theory]
        [InlineData("Function1", "Function1", "en-US", true)]
        [InlineData("function1", "Function1", "en-US", true)]
        [InlineData("Função", "FunçÃo", "pt-BR", true)]
        [InlineData("HttptRIGGER", "Httptrigger", "ja-JP", true)]
        [InlineData("Iasdf1", "iasdf1", "tr-TR", true)]
        public void FunctionNamesMatch_ReturnsExpectedResult(string functionNameA, string functionNameB, string cultureInfo, bool expectMatch)
        {
            CultureInfo environmentCulture = Thread.CurrentThread.CurrentCulture;

            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo(cultureInfo);
                Assert.Equal(expectMatch, Utility.FunctionNamesMatch(functionNameA, functionNameB));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = environmentCulture;
            }
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
        [InlineData("00:02:00", "00:02:00")]
        [InlineData(null, "10675199.02:48:05.4775807")]
        public void ComputeBackoff_Overflow(string maxValue, string expected)
        {
            TimeSpan? max = null;
            if (maxValue != null)
            {
                max = TimeSpan.Parse(maxValue);
            }

            TimeSpan expectedValue = TimeSpan.Parse(expected);

            // Catches two overflow bugs:
            // 1. Computed ticks would fluctuate between positive and negative, resulting in min-and-max alternating.
            // 2. At 64+ we'd throw an OverflowException.
            for (int i = 60; i < 70; i++)
            {
                TimeSpan result = Utility.ComputeBackoff(i, min: TimeSpan.FromSeconds(1), max: max);
                Assert.Equal(expectedValue, result);
            }
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
        [InlineData("", null, false)]
        [InlineData(null, null, false)]
        [InlineData("http://storage.blob.core.windows.net/functions/func.zip?sr=c&si=policy&sig=f%2BGLvBih%2BoFuQvckBSHWKMXwqGJHlPkESmZh9pjnHuc%3D",
            "http://storage.blob.core.windows.net", true)]
        [InlineData("http://storage.blob.core.windows.net/functions/func.zip",
            "http://storage.blob.core.windows.net", true)]
        [InlineData("https://storage.blob.core.windows.net/functions/func.zip",
            "https://storage.blob.core.windows.net", true)]
        [InlineData("https://storage.blob.core.windows.net/functions/func.zip?",
            "https://storage.blob.core.windows.net", true)]
        public void GetUriHostTests(string url, string expectedHost, bool expectedSuccess)
        {
            var success = Utility.TryGetUriHost(url, out var host);
            Assert.Equal(expectedSuccess, success);
            Assert.Equal(expectedHost, host);
        }

        [Theory]
        [InlineData("http://storage.blob.core.windows.net/functions/func.zip?sr=c&si=policy&sig=f%2BGLvBih%2BoFuQvckBSHWKMXwqGJHlPkESmZh9pjnHuc%3D", true)]
        [InlineData("http://storage.blob.core.windows.net/functions/func.zip?sr=c&si=policy&sv=abcd&sig=f%2BGLvBih%2BoFuQvckBSHWKMXwqGJHlPkESmZh9pjnHuc%3D", false)]
        [InlineData("http://storage.blob.core.windows.net/functions/func.zip", true)]
        public void GetAccountNameFromDomain(string url, bool isUrlWithNoSas)
        {
            Assert.Equal(isUrlWithNoSas, Utility.IsResourceAzureBlobWithoutSas(new Uri(url)));
        }

        [Theory]
        [InlineData("BlobEndpoint=https://storage.blob.core.windows.net;SharedAccessSignature=sv=2015-07-08&sig=f%2BGLvBih%2BoFuQvckBSHWKMXwqGJHlPkESmZh9pjnHuc%3D&spr=https&st=2016-04-12T03%3A24%3A31Z&se=2023-07-20T05:27:05Z&srt=s&ss=bf&sp=rwl", "2023-07-20T05:27:05Z", true)]
        [InlineData("BlobEndpoint=https://storage.blob.core.windows.net;SharedAccessSignature=sv=2015-07-08&sig=f%2BGLvBih%2BoFuQvckBSHWKMXwqGJHlPkESmZh9pjnHuc%3D&spr=https&st=2016-04-12T03%3A24%3A31Z&srt=s&ss=bf&sp=rwl", null, true)]
        [InlineData("BlobEndpoint=https://storage.blob.core.windows.net;TableEndpoint=https://table.core.windows.net;SharedAccessSignature=sv=2015-07-08&sig=f%2BGLvBih%2BoFuQvckBSHWKMXwqGJHlPkESmZh9pjnHuc%3D&spr=https&st=2016-04-12T03%3A24%3A31Z&srt=s&ss=bf&sp=rwl", null, true)]
        [InlineData("BlobEndpoint=https://storage.blob.core.windows.net;TableEndpoint=https://table.core.windows.net;SharedAccessSignature=sv=2015-07-08&sig=f%2BGLvBih%2BoFuQvckBSHWKMXwqGJHlPkESmZh9pjnHuc%3D&spr=https&st=2016-04-12T03%3A24%3A31Z&se=2023-07-20T05:27:05Z&srt=s&ss=bf&sp=rwl", "2023-07-20T05:27:05Z", true)]
        [InlineData("UseDevelopmentStorage=true", null, true)]
        [InlineData("https://storage.blob.core.windows.net/func/func.zip?sp=r&st=2023-07-12T21:27:05Z&se=2023-07-20T05:27:05Z&spr=https&sv=2022-11-02&sr=b&sig=f%2BGLvBih%2BoFuQvckBSHWKMXwqGJHlPkESmZh9pjnHuc%3D", "2023-07-20T05:27:05Z", false)]
        [InlineData("https://storage.blob.core.windows.net/functions/func.zip", null, false)]
        public void GetSasTokenExpirationDateFromSasSignature(string input, string expirationDate, bool isAzureWebJobsStorage)
        {
            Assert.Equal(expirationDate, Utility.GetSasTokenExpirationDate(input, isAzureWebJobsStorage));
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
        [InlineData("")]
        [InlineData("host")]
        [InlineData("Host")]
        [InlineData("-function")]
        [InlineData("_function")]
        [InlineData("function test")]
        [InlineData("function.test")]
        [InlineData("function0.1")]
        public void ValidateFunctionName_ThrowsOnInvalidName(string functionName)
        {
            var ex = Assert.Throws<InvalidOperationException>(() =>
            {
                Utility.ValidateName(functionName);
            });

            Assert.Equal(string.Format("'{0}' is not a valid function name.", functionName), ex.Message);
        }

        [Theory]
        [InlineData("testwithhost")]
        [InlineData("hosts")]
        [InlineData("myfunction")]
        [InlineData("myfunction-test")]
        [InlineData("myfunction_test")]
        public void ValidateFunctionName_DoesNotThrowOnValidName(string functionName)
        {
            try
            {
                Utility.ValidateName(functionName);
            }
            catch (InvalidOperationException)
            {
                Assert.True(false, $"Valid function name {functionName} failed validation.");
            }
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("_binding")]
        [InlineData("binding-test")]
        [InlineData("binding name")]
        public void ValidateBinding_InvalidName_Throws(string bindingName)
        {
            BindingMetadata bindingMetadata = new BindingMetadata
            {
                Name = bindingName
            };

            var ex = Assert.Throws<ArgumentException>(() =>
            {
                Utility.ValidateBinding(bindingMetadata);
            });

            Assert.Equal($"The binding name {bindingName} is invalid. Please assign a valid name to the binding.", ex.Message);
        }

        [Theory]
        [InlineData("bindingName")]
        [InlineData("binding1")]
        [InlineData(ScriptConstants.SystemReturnParameterBindingName)]
        public void ValidateBinding_ValidName_DoesNotThrow(string bindingName)
        {
            BindingMetadata bindingMetadata = new BindingMetadata
            {
                Name = bindingName,
                Type = "Blob"
            };

            if (bindingMetadata.IsReturn)
            {
                bindingMetadata.Direction = BindingDirection.Out;
            }

            try
            {
                Utility.ValidateBinding(bindingMetadata);
            }
            catch (ArgumentException)
            {
                Assert.True(false, $"Valid binding name '{bindingName}' failed validation.");
            }
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
        [InlineData(null)]
        [InlineData("java")]
        public void GetWorkerRuntimeTests(string workerRuntime)
        {
            FunctionMetadata func1 = new FunctionMetadata()
            {
                Name = "func1",
                Language = workerRuntime
            };

            IEnumerable<FunctionMetadata> functionMetadatas = new List<FunctionMetadata>
            {
                 func1
            };

            var testEnv = new TestEnvironment();
            testEnv.SetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime, workerRuntime);
            Assert.True(Utility.GetWorkerRuntime(functionMetadatas, testEnv) == workerRuntime);
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

        [Fact]
        public void GetValidFunctions_Returns_Expected()
        {
            FunctionMetadata func1 = new FunctionMetadata()
            {
                Name = "func1",
                Language = "java"
            };
            FunctionMetadata func2 = new FunctionMetadata()
            {
                Name = "func2",
                Language = "node"
            };

            FunctionDescriptor fd = new FunctionDescriptor();
            fd.Metadata = func1;

            IEnumerable<FunctionMetadata> functionMetadatas = new List<FunctionMetadata>
            {
                 func1, func2
            };
            ICollection<FunctionDescriptor> functionDescriptors = new List<FunctionDescriptor>
            {
                 fd
            };
            IEnumerable<FunctionMetadata> validFunctions = Utility.GetValidFunctions(functionMetadatas, functionDescriptors);
            int validFunctionsCount = 0;
            foreach (var metadata in validFunctions)
            {
                Assert.Equal(func1.Name, metadata.Name);
                validFunctionsCount++;
            }
            Assert.True(validFunctionsCount == 1);
        }

        [Fact]
        public void GetValidFunctions_Returns_Null()
        {
            FunctionMetadata func1 = new FunctionMetadata()
            {
                Name = "func1",
                Language = "java"
            };
            FunctionMetadata func2 = new FunctionMetadata()
            {
                Name = "func2",
                Language = "node"
            };
            FunctionMetadata func3 = new FunctionMetadata()
            {
                Name = "func3",
                Language = "node"
            };

            FunctionDescriptor fd = new FunctionDescriptor();
            fd.Metadata = func3;

            IEnumerable<FunctionMetadata> functionMetadatas = new List<FunctionMetadata>
            {
                 func1, func2
            };
            ICollection<FunctionDescriptor> functionDescriptors = new List<FunctionDescriptor>
            {
                 fd
            };

            IEnumerable<FunctionMetadata> validFunctions = Utility.GetValidFunctions(null, functionDescriptors);
            Assert.Null(validFunctions);

            validFunctions = Utility.GetValidFunctions(functionMetadatas, null);
            Assert.Null(validFunctions);

            validFunctions = Utility.GetValidFunctions(functionMetadatas, functionDescriptors);
            Assert.Empty(validFunctions);
        }

        [Theory]
        [InlineData("", "", "", "DefaultEndpointsProtocol=https;AccountName=;AccountKey=;EndpointSuffix=")]
        [InlineData(null, null, null, "DefaultEndpointsProtocol=https;AccountName=;AccountKey=;EndpointSuffix=")]
        [InlineData("accountname", "password", CloudConstants.AzureStorageSuffix, "DefaultEndpointsProtocol=https;AccountName=accountname;AccountKey=password;EndpointSuffix=core.windows.net")]
        [InlineData("accountname", "password", CloudConstants.BlackforestStorageSuffix, "DefaultEndpointsProtocol=https;AccountName=accountname;AccountKey=password;EndpointSuffix=core.cloudapi.de")]
        [InlineData("accountname", "password", CloudConstants.FairfaxStorageSuffix, "DefaultEndpointsProtocol=https;AccountName=accountname;AccountKey=password;EndpointSuffix=core.usgovcloudapi.net")]
        [InlineData("accountname", "password", CloudConstants.MooncakeStorageSuffix, "DefaultEndpointsProtocol=https;AccountName=accountname;AccountKey=password;EndpointSuffix=core.chinacloudapi.cn")]
        [InlineData("accountname", "password", CloudConstants.USSecStorageSuffix, "DefaultEndpointsProtocol=https;AccountName=accountname;AccountKey=password;EndpointSuffix=core.microsoft.scloud")]
        [InlineData("accountname", "password", CloudConstants.USNatStorageSuffix, "DefaultEndpointsProtocol=https;AccountName=accountname;AccountKey=password;EndpointSuffix=core.eaglex.ic.gov")]
        public void BuildStorageConnectionString(string accountName, string accessKey, string suffix, string expectedConnectionString)
        {
            Assert.Equal(expectedConnectionString, Utility.BuildStorageConnectionString(accountName, accessKey, suffix));
        }

        [Theory]
        [InlineData("functionName", true)]
        [InlineData(LogConstants.NameKey, true)]
        [InlineData(ScopeKeys.FunctionName, true)]
        [InlineData("somekey", false)]
        public void IsFunctionName_ReturnsExpectedResult(string keyName, bool expected)
        {
            var kvp = new KeyValuePair<string, object>(keyName, "test");
            Assert.Equal(expected, Utility.IsFunctionName(kvp));
        }

        [Theory]
        [InlineData("functionName", true)]
        [InlineData(LogConstants.NameKey, true)]
        [InlineData(ScopeKeys.FunctionName, true)]
        [InlineData("somekey", false)]
        public void TryGetFunctionName_ReturnsExpectedResult(string keyName, bool expected)
        {
            var scopeProps = new Dictionary<string, object>
            {
                { keyName, "test" },
                { "someprop", "somevalue" },
                { "anotherprop", "anothervalue" }
            };
            Assert.Equal(expected, Utility.TryGetFunctionName(scopeProps, out string functionName));
            if (expected)
            {
                Assert.Equal("test", functionName);
            }
        }

        [Fact]
        public async Task Test_LogAutorestGeneratedJson_Without_AutorestGeneratedJson()
        {
            string functionAppDirectory = GetTemporaryDirectory();
            try
            {
                Utility.LogAutorestGeneratedJsonIfExists(functionAppDirectory, _testLogger);
                var allLogs = _testLogger.GetLogMessages();
                Assert.Empty(allLogs);
            }
            finally
            {
                await FileUtility.DeleteDirectoryAsync(functionAppDirectory, true);
            }
        }

        [Theory]
        [InlineData("{\"name\":\"@autorest/azure-functions-csharp\",\"version\":\"0.2.0-preview\"}", "autorest_generated.json file found generated by Autorest (https://aka.ms/stencil) | file content", LogLevel.Information)]
        [InlineData("{\"name\":\"@autorest/azure-functions-csharp\",\"version\"\"0.2.0-preview\"}", "autorest_generated.json file found is incorrect (https://aka.ms/stencil) | exception", LogLevel.Warning)]
        public async Task Test_LogAutorestGeneratedJson_With_AutorestGeneratedJson(string autorestGeneratedJsonContent, string expectedContents, LogLevel expectedLogLevel)
        {
            string functionAppDirectory = GetTemporaryDirectory();
            try
            {
                File.WriteAllText(Path.Combine(functionAppDirectory, ScriptConstants.AutorestGeenratedMetadataFileName), autorestGeneratedJsonContent);
                Utility.LogAutorestGeneratedJsonIfExists(functionAppDirectory, _testLogger);
                var allLogs = _testLogger.GetLogMessages();
                VerifyLogLevel(allLogs, expectedContents, expectedLogLevel);
            }
            finally
            {
                await FileUtility.DeleteDirectoryAsync(functionAppDirectory, true);
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void IsCodelessDotNetLanguageFunction_Returns_Expected(bool setIsCodeless)
        {
            FunctionMetadata func1 = new FunctionMetadata()
            {
                Name = "func1",
                Language = DotNetScriptTypes.CSharp
            };

            FunctionMetadata func2 = new FunctionMetadata()
            {
                Name = "func2",
                Language = DotNetScriptTypes.DotNetAssembly
            };

            FunctionMetadata nodeFunc = new FunctionMetadata()
            {
                Name = "func3",
                Language = "node"
            };

            if (setIsCodeless)
            {
                func1.Properties.Add("IsCodeless", true);
                func2.Properties.Add("IsCodeless", true);
                nodeFunc.Properties.Add("IsCodeless", true);
                Assert.True(Utility.IsCodelessDotNetLanguageFunction(func1));
                Assert.True(Utility.IsCodelessDotNetLanguageFunction(func2));
            }
            else
            {
                Assert.False(Utility.IsCodelessDotNetLanguageFunction(func1));
                Assert.False(Utility.IsCodelessDotNetLanguageFunction(func2));
                Assert.False(Utility.IsCodelessDotNetLanguageFunction(nodeFunc));
            }
        }

        [Theory]
        [InlineData(false, false, FunctionAppContentEditingState.NotAllowed)]
        [InlineData(false, true, FunctionAppContentEditingState.Allowed)]
        [InlineData(true, true, FunctionAppContentEditingState.NotAllowed)]
        [InlineData(true, false, FunctionAppContentEditingState.NotAllowed)]
        [InlineData(true, true, FunctionAppContentEditingState.Unknown, false)]
        public void GetFunctionAppContentEditingState_Returns_Expected(bool isFileSystemReadOnly, bool azureFilesAppSettingsExist, FunctionAppContentEditingState isFunctionAppContentEditable, bool isLinuxConsumption = true)
        {
            var environment = new TestEnvironment();
            if (isLinuxConsumption)
            {
                environment.SetEnvironmentVariable(EnvironmentSettingNames.ContainerName, "test-container");
            }
            if (azureFilesAppSettingsExist)
            {
                environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureFilesConnectionString, "test value");
                environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureFilesContentShare, "test value");
            }

            var applicationHostOptions = new ScriptApplicationHostOptions
            {
                IsFileSystemReadOnly = isFileSystemReadOnly
            };
            var optionsWrapper = new OptionsWrapper<ScriptApplicationHostOptions>(applicationHostOptions);

            Assert.Equal(Utility.GetFunctionAppContentEditingState(environment, optionsWrapper), isFunctionAppContentEditable);
        }

        [Theory]
        [InlineData(false, true, false)]
        [InlineData(false, false, false)]
        [InlineData(true, false, false)]
        [InlineData(true, true, true)]
        public void VerifyWorkerIndexingDecisionLogic(bool workerIndexingFeatureFlag, bool workerIndexingConfigProperty, bool expected)
        {
            var testEnv = new TestEnvironment();
            testEnv.SetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime, RpcWorkerConstants.PythonLanguageWorkerName);
            if (workerIndexingFeatureFlag)
            {
                testEnv.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsFeatureFlags, ScriptConstants.FeatureFlagEnableWorkerIndexing);
            }
            RpcWorkerConfig workerConfig = new RpcWorkerConfig() { Description = TestHelpers.GetTestWorkerDescription("python", "none", workerIndexingConfigProperty) };
            bool workerShouldIndex = Utility.CanWorkerIndex(new List<RpcWorkerConfig>() { workerConfig }, testEnv, new FunctionsHostingConfigOptions());
            Assert.Equal(expected, workerShouldIndex);
        }

        [Theory]
        [InlineData(true, true, false, "", true)]
        [InlineData(true, true, true, "NonApp", true)]
        [InlineData(true, true, true, "AppName", true)]
        [InlineData(true, true, false, "NonApp", true)]
        [InlineData(true, true, false, "AppName", true)]
        public void VerifyWorkerIndexingFeatureFlagTakesPrecedence(bool workerIndexingFeatureFlag, bool workerIndexingConfigProperty, bool enabledHostingConfig, string disabledHostingConfig, bool expected)
        {
            VerifyCanWorkerIndexUtility(workerIndexingFeatureFlag, workerIndexingConfigProperty, enabledHostingConfig, disabledHostingConfig, expected);
        }

        [Theory]
        [InlineData(true, false, false, "", false)]
        [InlineData(true, false, true, "NonApp", false)]
        [InlineData(true, false, true, "AppName", false)]
        [InlineData(true, false, false, "NonApp", false)]
        [InlineData(true, false, false, "AppName", false)]
        [InlineData(true, true, false, "AppName", true)]
        public void VerifyWorkerConfigTakesPrecedence(bool workerIndexingFeatureFlag, bool workerIndexingConfigProperty, bool enabledHostingConfig, string disabledHostingConfig, bool expected)
        {
            VerifyCanWorkerIndexUtility(workerIndexingFeatureFlag, workerIndexingConfigProperty, enabledHostingConfig, disabledHostingConfig, expected);
        }

        [Theory]
        [InlineData(false, true, true, "", true)]
        [InlineData(false, true, true, "NonApp", true)]
        [InlineData(false, true, false, "NonApp", false)]
        [InlineData(false, false, false, "NonApp", false)]
        public void VerifyStampLevelHostingConfigHonored(bool workerIndexingFeatureFlag, bool workerIndexingConfigProperty, bool enabledHostingConfig, string disabledHostingConfig, bool expected)
        {
            VerifyCanWorkerIndexUtility(workerIndexingFeatureFlag, workerIndexingConfigProperty, enabledHostingConfig, disabledHostingConfig, expected);
        }

        [Theory]
        [InlineData(false, true, true, "", true)]
        [InlineData(false, true, true, "NonApp|AppName", false)]
        [InlineData(false, true, true, "NonApp|AnotherAppName", true)]
        [InlineData(false, true, true, "nonapp|AppName", false)]
        [InlineData(false, true, true, "appname", false)]
        [InlineData(false, true, true, "nonapp|appname", false)]
        [InlineData(false, true, true, "NonApp|anotherAppname", true)]
        [InlineData(false, false, true, "NonApp", false)]
        [InlineData(false, false, true, "AppName", false)]
        [InlineData(false, false, false, "Appname", false)]
        [InlineData(false, true, false, "AppName", false)]
        [InlineData(false, true, true, "AppName", false)]
        public void VerifyDisabledAppConfigHonored(bool workerIndexingFeatureFlag, bool workerIndexingConfigProperty, bool enabledHostingConfig, string disabledHostingConfig, bool expected)
        {
            VerifyCanWorkerIndexUtility(workerIndexingFeatureFlag, workerIndexingConfigProperty, enabledHostingConfig, disabledHostingConfig, expected);
        }

        private void VerifyCanWorkerIndexUtility(bool workerIndexingFeatureFlag, bool workerIndexingConfigProperty, bool enabledHostingConfig, string disabledHostingConfig, bool expected)
        {
            var testEnv = new TestEnvironment();
            testEnv.SetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime, RpcWorkerConstants.PythonLanguageWorkerName);
            string appName = "AppName";
            if (workerIndexingFeatureFlag)
            {
                testEnv.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsFeatureFlags, ScriptConstants.FeatureFlagEnableWorkerIndexing);
            }

            testEnv.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteName, appName);

            RpcWorkerConfig workerConfig = new RpcWorkerConfig() { Description = TestHelpers.GetTestWorkerDescription("python", "none", workerIndexingConfigProperty) };
            var hostingOptions = new FunctionsHostingConfigOptions();
            if (enabledHostingConfig)
            {
                hostingOptions.Features.Add(RpcWorkerConstants.WorkerIndexingEnabled, "1");
            }

            hostingOptions.Features.Add(RpcWorkerConstants.WorkerIndexingDisabledApps, disabledHostingConfig);

            bool workerShouldIndex = Utility.CanWorkerIndex(new List<RpcWorkerConfig>() { workerConfig }, testEnv, hostingOptions);
            Assert.Equal(expected, workerShouldIndex);
        }

        [Theory]
        [InlineData(true, false)]
        [InlineData(false, false)]
        public void WorkerIndexingDecisionLogic_NullConfig(bool workerIndexingFeatureFlag, bool expected)
        {
            var testEnv = new TestEnvironment();
            testEnv.SetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime, RpcWorkerConstants.PythonLanguageWorkerName);
            if (workerIndexingFeatureFlag)
            {
                testEnv.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsFeatureFlags, ScriptConstants.FeatureFlagEnableWorkerIndexing);
            }
            bool workerShouldIndex = Utility.CanWorkerIndex(null, testEnv, new FunctionsHostingConfigOptions());
            Assert.Equal(expected, workerShouldIndex);
        }

        [Theory]
        [InlineData(true, false)]
        [InlineData(false, false)]
        public void WorkerIndexingDecisionLogic_NullConfigDescription(bool workerIndexingFeatureFlag, bool expected)
        {
            var testEnv = new TestEnvironment();
            testEnv.SetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime, RpcWorkerConstants.PythonLanguageWorkerName);
            if (workerIndexingFeatureFlag)
            {
                testEnv.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsFeatureFlags, ScriptConstants.FeatureFlagEnableWorkerIndexing);
            }
            RpcWorkerConfig workerConfig = new RpcWorkerConfig();
            bool workerShouldIndex = Utility.CanWorkerIndex(new List<RpcWorkerConfig>() { workerConfig }, testEnv, new FunctionsHostingConfigOptions());
            Assert.Equal(expected, workerShouldIndex);
        }

        [Theory]
        [InlineData(true, false)]
        [InlineData(false, false)]
        public void WorkerIndexingDecisionLogic_NullWorkerIndexingProperty(bool workerIndexingFeatureFlag, bool expected)
        {
            var testEnv = new TestEnvironment();
            testEnv.SetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime, RpcWorkerConstants.PythonLanguageWorkerName);
            if (workerIndexingFeatureFlag)
            {
                testEnv.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsFeatureFlags, ScriptConstants.FeatureFlagEnableWorkerIndexing);
            }
            RpcWorkerConfig workerConfig = new RpcWorkerConfig()
            {
                Description = new RpcWorkerDescription()
                {
                    Extensions = new List<string>(),
                    Language = "python",
                    WorkerDirectory = "testDir",
                    WorkerIndexing = null
                }
            };
            bool workerShouldIndex = Utility.CanWorkerIndex(new List<RpcWorkerConfig>() { workerConfig }, testEnv, new FunctionsHostingConfigOptions());
            Assert.Equal(expected, workerShouldIndex);
        }

        [Theory]
        [InlineData("True", true, true)]
        [InlineData("False", false, true)]
        [InlineData(true, true, true)]
        [InlineData(false, false, true)]
        [InlineData("blah", false, false)]
        [InlineData(null, false, false)]
        public void TryReadAsBool_ReturnsExpectedBoolValue(object value, bool expectedValueResult, bool expectedMethodResult)
        {
            var properties = new Dictionary<string, object>();
            properties.Add("MyValue", value);

            bool methodResult = Utility.TryReadAsBool(properties, "MyValue", out bool resultValue);

            Assert.Equal(methodResult, expectedMethodResult);
            Assert.Equal(resultValue, expectedValueResult);
        }

        [Fact]
        public void ValidateRetryOptions_ThrowsWhenMaxRetryIsNull()
        {
            var retryOptions = new RetryOptions
            {
                Strategy = RetryStrategy.FixedDelay
            };

            var ex = Assert.Throws<ArgumentNullException>(() => Utility.ValidateRetryOptions(retryOptions));
            Assert.Equal("Value cannot be null. (Parameter 'MaxRetryCount')", ex.Message);
        }

        [Fact]
        public void ValidateRetryOptions_ThrowsWhenFixedDelayArgsNull()
        {
            var retryOptions = new RetryOptions
            {
                Strategy = RetryStrategy.FixedDelay,
                MaxRetryCount = 5
            };

            var ex = Assert.Throws<ArgumentNullException>(() => Utility.ValidateRetryOptions(retryOptions));
            Assert.Equal("Value cannot be null. (Parameter 'DelayInterval')", ex.Message);
        }

        [Fact]
        public void ValidateRetryOptions_ThrowsWhenExponentialBackoffArgsNull()
        {
            var retryOptions1 = new RetryOptions
            {
                Strategy = RetryStrategy.ExponentialBackoff,
                MaxRetryCount = 5,
                MinimumInterval = TimeSpan.FromSeconds(5)
            };

            var retryOptions2 = new RetryOptions
            {
                Strategy = RetryStrategy.ExponentialBackoff,
                MaxRetryCount = 5,
                MaximumInterval = TimeSpan.MaxValue
            };

            var ex1 = Assert.Throws<ArgumentNullException>(() => Utility.ValidateRetryOptions(retryOptions1));
            Assert.Equal("Value cannot be null. (Parameter 'MaximumInterval')", ex1.Message);

            var ex2 = Assert.Throws<ArgumentNullException>(() => Utility.ValidateRetryOptions(retryOptions2));
            Assert.Equal("Value cannot be null. (Parameter 'MinimumInterval')", ex2.Message);
        }

        [Fact]
        public void ValidateRetryOptions_FixedDelaySuccess()
        {
            var retryOptions = new RetryOptions
            {
                Strategy = RetryStrategy.FixedDelay,
                MaxRetryCount = 5,
                DelayInterval = TimeSpan.FromSeconds(600)
            };

            Utility.ValidateRetryOptions(retryOptions);
        }

        [Fact]
        public void ValidateRetryOptions_ExponentialBackoffSuccess()
        {
            var retryOptions = new RetryOptions
            {
                Strategy = RetryStrategy.ExponentialBackoff,
                MaxRetryCount = 5,
                MinimumInterval = TimeSpan.FromSeconds(10),
                MaximumInterval = TimeSpan.MaxValue
            };

            Utility.ValidateRetryOptions(retryOptions);
        }

        private static void VerifyLogLevel(IList<LogMessage> allLogs, string msg, LogLevel expectedLevel)
        {
            var message = allLogs.FirstOrDefault(l => l.FormattedMessage.Contains(msg));
            Assert.NotNull(message);
            Assert.Equal(expectedLevel, message.Level);
        }

        private static string GetTemporaryDirectory()
        {
            string tempDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            FileUtility.EnsureDirectoryExists(tempDirectory);
            return tempDirectory;
        }
    }
}
