// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Abstractions.Description;
using Microsoft.Azure.WebJobs.Script.Binding;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class FunctionGeneratorTests
    {
        [Fact]
        public void Generate_WithMultipleOutParameters()
        {
            string functionName = "FunctionWithOuts";
            Collection<ParameterDescriptor> parameters = new Collection<ParameterDescriptor>();

            parameters.Add(new ParameterDescriptor("param1", typeof(string)));
            parameters.Add(new ParameterDescriptor("param2", typeof(string).MakeByRefType()) { Attributes = ParameterAttributes.Out });
            parameters.Add(new ParameterDescriptor("param3", typeof(string).MakeByRefType()) { Attributes = ParameterAttributes.Out });

            FunctionMetadata metadata = new FunctionMetadata();
            TestInvoker invoker = new TestInvoker();
            FunctionDescriptor function = new FunctionDescriptor(functionName, invoker, metadata, parameters, null, null, null);
            Collection<FunctionDescriptor> functions = new Collection<FunctionDescriptor>();
            functions.Add(function);

            // Make sure we don't generate a TimeoutAttribute if FunctionTimeout is null.
            var scriptConfig = new ScriptJobHostOptions();
            scriptConfig.FunctionTimeout = null;
            Collection<CustomAttributeBuilder> typeAttributes = new Collection<CustomAttributeBuilder>();

            // generate the Type
            Type functionType = FunctionGenerator.Generate("TestScriptHost", "TestFunctions", typeAttributes, functions);

            // verify the generated function
            MethodInfo method = functionType.GetMethod(functionName);
            IEnumerable<Attribute> attributes = functionType.GetCustomAttributes();
            Assert.Empty(attributes);
            ParameterInfo[] functionParams = method.GetParameters();

            // Verify that we have the correct number of parameters
            Assert.Equal(parameters.Count, functionParams.Length);

            // Verify that out parameters were correctly generated
            Assert.True(functionParams[1].IsOut);
            Assert.True(functionParams[2].IsOut);

            // Verify that the method is invocable
            method.Invoke(null, new object[] { "test", null, null });

            // verify our custom invoker was called
            Assert.Equal(1, invoker.InvokeCount);
        }

        [Theory]
        [InlineData("User1", typeof(Task<string>), "User1")]
        [InlineData("User2", typeof(Task<string>), "User2")]
        [InlineData("User3", typeof(Task), null)]
        [InlineData("User4", typeof(Task), null)]
        public async Task Generate_WithReturnValue(string userFuncName, Type generatedMethodReturnType, string expectedResult)
        {
            var userFunc = this.GetType().GetMethod(userFuncName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

            string functionName = "FunctionWithStrReturn";
            Collection<ParameterDescriptor> parameters = new Collection<ParameterDescriptor>();

            var userRetType = userFunc.ReturnType;
            ParameterDescriptor returnParameter;
            if (DotNetFunctionDescriptorProvider.TryCreateReturnValueParameterDescriptor(userRetType, new FunctionBinding[0], out returnParameter))
            {
                parameters.Add(returnParameter);
            }

            FunctionMetadata metadata = new FunctionMetadata();
            var invoker = new RealInvoker(userFunc);
            FunctionDescriptor function = new FunctionDescriptor(functionName, invoker, metadata, parameters, null, null, null);
            Collection<FunctionDescriptor> functions = new Collection<FunctionDescriptor>();
            functions.Add(function);

            // generate the Type
            Type functionType = FunctionGenerator.Generate("TestScriptHost", "TestFunctions", null, functions);

            // verify the generated function
            MethodInfo method = functionType.GetMethod(functionName);
            IEnumerable<Attribute> attributes = functionType.GetCustomAttributes();
            Assert.Empty(attributes);
            ParameterInfo[] functionParams = method.GetParameters();

            // One input parameter
            Assert.Equal(0, functionParams.Length);

            // The final generated function is always Task
            Assert.Equal(generatedMethodReturnType, method.ReturnType);

            // Verify that the method is invocable
            var result = method.Invoke(null, new object[] { });

            Task<object> taskResult = Unwrap(result);
            var realResult = await taskResult;

            Assert.Equal(expectedResult, realResult);
        }

        internal static async Task<object> Unwrap(object result)
        {
            // unwrap the task
            if (result is Task)
            {
                result = await ((Task)result).ContinueWith(t => DotNetFunctionInvoker.GetTaskResult(t), TaskContinuationOptions.ExecuteSynchronously);
            }

            return result;
        }

        // Sample user functions for various return signatures.
        public static void User4()
        {
        }

        public static Task User3()
        {
            return Task.FromResult<string>(null);
        }

        public static Task<string> User2()
        {
            return Task.FromResult<string>("User2");
        }

        public static string User1()
        {
            return "User1";
        }

        // An IFunctionInvoker that invokes the methodInfo.
        // This simulates how DotNetInvoker wraps a user's C# method.
        public class RealInvoker : IFunctionInvoker
        {
            private readonly MethodInfo _innerMethod;

            public RealInvoker(MethodInfo innerMethod)
            {
                _innerMethod = innerMethod;
            }

            public ILogger FunctionLogger => throw new NotImplementedException();

            public Task<object> Invoke(object[] parameters)
            {
                var obj = _innerMethod.Invoke(null, new object[0]);
                return Unwrap(obj);
            }

            public void OnError(Exception ex)
            {
                throw new NotImplementedException();
            }
        }
    }
}
