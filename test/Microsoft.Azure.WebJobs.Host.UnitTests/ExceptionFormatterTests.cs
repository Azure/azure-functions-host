// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Host.Diagnostics;
using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests
{
    public class ExceptionFormatterTests
    {
        [Fact]
        public void FormatException_RemovesAsyncFrames()
        {
            try
            {
                var test = new TestClass();
                test.Run();
            }
            catch (Exception exc)
            {
                string formattedException = ExceptionFormatter.GetFormattedException(exc);

                Assert.DoesNotMatch("d__.\\.MoveNext()", formattedException);
                Assert.DoesNotContain("TaskAwaiter", formattedException);
            }
        }

        [Fact]
        public void FormatException_ResolvesAsyncMethodNames()
        {
            try
            {
                var test = new TestClass();
                test.Run();
            }
            catch (Exception exc)
            {
                string formattedException = ExceptionFormatter.GetFormattedException(exc);

                string typeName = $"{typeof(TestClass).DeclaringType.FullName}.{ nameof(TestClass)}";
                Assert.Contains($"async {typeName}.{nameof(TestClass.Run1Async)}()", formattedException);
                Assert.Contains($"async {typeName}.{nameof(TestClass.Run2Async)}()", formattedException);
                Assert.Contains($"async {typeName}.{nameof(TestClass.CrashAsync)}()", formattedException);
            }
        }

        [Fact]
        public void FormatException_OutputsMethodParameters()
        {
            try
            {
                var test = new TestClass();
                test.Run();
            }
            catch (Exception exc)
            {
                string formattedException = ExceptionFormatter.GetFormattedException(exc);
                
                Assert.Contains($"{nameof(TestClass.Run)}(String arg)", formattedException);
            }
        }

        [Fact]
        public void FormatException_OutputsExpectedAsyncMethodParameters()
        {
            try
            {
                var test = new TestClass();
                test.Run("Test2");
            }
            catch (Exception exc)
            {
                string formattedException = ExceptionFormatter.GetFormattedException(exc);

                Assert.Contains($"{nameof(TestClass.Run4Async)}(String arg)", formattedException);

                // When unable to resolve, the '??' token is used
                
                Assert.Contains($"{nameof(TestClass.Run5Async)}(??)", formattedException);
            }
        }

        private class TestClass
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            public void Run()
            {
                Run("Test1");
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public void Run(string arg)
            {
                if (string.Equals(arg, "Test1"))
                {
                    Run1Async().Wait();
                }
                else if (string.Equals(arg, "Test2"))
                {
                    Run4Async("Arg").Wait();
                }
                else if (string.Equals(arg, "Test3"))
                {
                    try
                    {
                        Run1();
                    }
                    catch (Exception exc)
                    {
                        // Test with inner exception
                        throw new Exception("Crash!", exc);
                    }
                }
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private void Run1()
            {
                throw new Exception("Sync crash!");
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public async Task Run1Async()
            {
                await Run2Async();
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public async Task Run2Async()
            {
                await CrashAsync();
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public async Task CrashAsync()
            {
                await Task.Yield();
                throw new Exception("Crash!");
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public async Task Run4Async(string arg)
            {
                await Run5Async();
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public async Task Run5Async()
            {
                await CrashAsync();
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public async Task Run5Async(string arg)
            {
                await CrashAsync();
            }
        }
    }
}
