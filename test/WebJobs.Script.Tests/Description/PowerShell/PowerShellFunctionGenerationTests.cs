// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Description;
using Moq;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class PowerShellFunctionGenerationTests
    {
        private const string FunctionName = "TestFunction";
        private static readonly ScriptSettingsManager SettingsManager = ScriptSettingsManager.Instance;

        [Fact]
        public void GenerateHttpTriggerFunction()
        {
            BindingMetadata trigger = BindingMetadata.Create<HttpTriggerBindingMetadata>(new JObject
            {
                { "type", "HttpTrigger" },
                { "name", "req" }
            });

            MethodInfo method = GenerateMethod(trigger);

            VerifyCommonProperties(method);

            // verify trigger parameter
            ParameterInfo parameter = method.GetParameters()[0];
            Assert.Equal("req", parameter.Name);
            Assert.Equal(typeof(HttpRequestMessage), parameter.ParameterType);
            NoAutomaticTriggerAttribute attribute = method.GetCustomAttribute<NoAutomaticTriggerAttribute>();
            Assert.NotNull(attribute);
        }

        [Fact]
        public void GenerateQueueTriggerFunction()
        {
            string inputBindingName = "inputData";
            BindingMetadata trigger = BindingMetadata.Create<BindingMetadata>(new JObject
            {
                { "type", "QueueTrigger" },
                { "name", inputBindingName },
                { "direction", "in" },
                { "queueName", "test" }
            });

            MethodInfo method = GenerateMethod(trigger);

            VerifyCommonProperties(method);

            // verify trigger parameter
            ParameterInfo parameter = method.GetParameters()[0];
            Assert.Equal(inputBindingName, parameter.Name);
            Assert.Equal(typeof(string), parameter.ParameterType);
            QueueTriggerAttribute attribute = parameter.GetCustomAttribute<QueueTriggerAttribute>();
            Assert.Equal("test", attribute.QueueName);
        }

        [Fact]
        public void GenerateQueueTriggerFunction_WithInvalidInputName_Fails()
        {
            string inputBindingName = "input";
            string expectedError = "Input binding name 'input' is not allowed.";

            BindingMetadata trigger = new BindingMetadata
            {
                Type = "QueueTrigger",
                Name = inputBindingName
            };
            trigger.Raw = new JObject
            {
                { "Type", "QueueTrigger" },
                { "Name", inputBindingName },
                { "Direction", "in" },
                { "QueueName", "test" }
            };

            using (var scriptHostInfo = GetScriptHostInfo())
            {
                Exception ex = Assert.Throws<InvalidOperationException>(() => GenerateMethod(trigger, scriptHostInfo));
                Assert.Equal("Sequence contains no elements", ex.Message);

                var functionError = scriptHostInfo.Host.FunctionErrors[FunctionName];
                Assert.True(functionError.Contains(expectedError));
            }
        }

        private static void VerifyCommonProperties(MethodInfo method)
        {
            Assert.Equal(FunctionName, method.Name);
            ParameterInfo[] parameters = method.GetParameters();
            Assert.Equal(4, parameters.Length);
            Assert.Equal(typeof(Task), method.ReturnType);

            // verify TextWriter parameter
            ParameterInfo parameter = parameters[1];
            Assert.Equal("_log", parameter.Name);
            Assert.Equal(typeof(TraceWriter), parameter.ParameterType);

            // verify IBinder parameter
            parameter = parameters[2];
            Assert.Equal("_binder", parameter.Name);
            Assert.Equal(typeof(IBinder), parameter.ParameterType);

            // verify ExecutionContext parameter
            parameter = parameters[3];
            Assert.Equal("_context", parameter.Name);
            Assert.Equal(typeof(ExecutionContext), parameter.ParameterType);
        }

        private static MethodInfo GenerateMethod(BindingMetadata trigger)
        {
            using (var scriptHostInfo = GetScriptHostInfo())
            {
                return GenerateMethod(trigger, scriptHostInfo);
            }
        }

        private static MethodInfo GenerateMethod(BindingMetadata trigger, ScriptHostInfo scriptHostInfo)
        {
            FunctionMetadata metadata = new FunctionMetadata();
            metadata.Name = FunctionName;
            metadata.ScriptFile = Path.Combine(scriptHostInfo.RootPath, @"Common\test.ps1");
            metadata.Bindings.Add(trigger);
            metadata.ScriptType = ScriptType.PowerShell;

            List<FunctionMetadata> functions = new List<FunctionMetadata>();
            functions.Add(metadata);
            FunctionDescriptorProvider[] descriptorProviders = new FunctionDescriptorProvider[]
            {
                new PowerShellFunctionDescriptorProvider(scriptHostInfo.Host, scriptHostInfo.Configuration)
            };

            var functionDescriptors = scriptHostInfo.Host.GetFunctionDescriptors(functions, descriptorProviders);
            Type t = FunctionGenerator.Generate("TestScriptHost", "Host.Functions", null, functionDescriptors);

            MethodInfo method = t.GetMethods(BindingFlags.Public | BindingFlags.Static).First();
            return method;
        }

        private static ScriptHostInfo GetScriptHostInfo()
        {
            var environment = new Mock<IScriptHostEnvironment>();
            string rootPath = Path.Combine(Environment.CurrentDirectory, @"TestScripts\PowerShell");
            ScriptHostConfiguration scriptConfig = new ScriptHostConfiguration()
            {
                RootScriptPath = rootPath
            };
            var host = ScriptHost.Create(environment.Object, scriptConfig, SettingsManager);
            return new ScriptHostInfo(host, scriptConfig, rootPath);
        }
    }

    [SuppressMessage("Microsoft.StyleCop.CSharp.MaintainabilityRules", "SA1402:FileMayOnlyContainASingleClass")]
    internal class ScriptHostInfo : IDisposable
    {
        public ScriptHostInfo(ScriptHost host, ScriptHostConfiguration config, string rootPath)
        {
            Host = host;
            Configuration = config;
            RootPath = rootPath;
        }

        public ScriptHost Host { get; }

        public ScriptHostConfiguration Configuration { get; }

        public string RootPath { get; }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                Host.Dispose();
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }
    }
}
