// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.IO;
using Microsoft.Azure.WebJobs.Script.Abstractions;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Microsoft.Extensions.Configuration;
using Microsoft.WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Rpc
{
    public class JavaWorkerProviderTests
    {
        [Fact]
        public void SetsDebugPort()
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new List<KeyValuePair<string, string>>()
                {
                    new KeyValuePair<string, string>("JAVA_HOME", "asdf"),
                    new KeyValuePair<string, string>("workers:java:debug", "1000")
                })
                .Build();

            var provider = new JavaWorkerProvider();
            var args = new ArgumentsDescription();
            var result = provider.TryConfigureArguments(args, config, new TestLogger("test"));

            Assert.True(result);
            Assert.Contains(args.ExecutableArguments, (exeArg) => exeArg.Contains("1000"));
        }

        [Fact]
        public void OverridesDebugWithJavaOpts()
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new List<KeyValuePair<string, string>>()
                {
                    new KeyValuePair<string, string>("JAVA_HOME", "asdf"),
                    new KeyValuePair<string, string>("JAVA_OPTS", "address=1001"),
                    new KeyValuePair<string, string>("workers:java:debug", "1000")
                })
                .Build();

            var provider = new JavaWorkerProvider();
            var args = new ArgumentsDescription();
            var result = provider.TryConfigureArguments(args, config, new TestLogger("test"));

            Assert.True(result);
            Assert.Contains(args.ExecutableArguments, (exeArg) => exeArg.Contains("address=1001"));
            Assert.DoesNotContain(args.ExecutableArguments, (exeArg) => exeArg.Contains("address=1000"));
        }

        [Fact]
        public void DisablesDebugIfInvalid()
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new List<KeyValuePair<string, string>>()
                {
                    new KeyValuePair<string, string>("JAVA_HOME", "asdf"),
                    new KeyValuePair<string, string>("workers:java:debug", "false")
                })
                .Build();

            var provider = new JavaWorkerProvider();
            var args = new ArgumentsDescription();
            var result = provider.TryConfigureArguments(args, config, new TestLogger("test"));

            Assert.True(result);
            Assert.DoesNotContain(args.ExecutableArguments, (exeArg) => exeArg.Contains("address="));
        }

        [Fact]
        public void FailsIfNoJavaHome()
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new List<KeyValuePair<string, string>>()
                {
                    new KeyValuePair<string, string>("workers:java:debug", "1000")
                })
                .Build();

            var provider = new JavaWorkerProvider();
            var args = new ArgumentsDescription();
            var result = provider.TryConfigureArguments(args, config, new TestLogger("test"));

            Assert.False(result);
        }

        [Fact]
        public void OverridesJavaHomeInAzure()
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new List<KeyValuePair<string, string>>()
                {
                    new KeyValuePair<string, string>("JAVA_HOME", "d:/java/jdk1.7.0"),
                    new KeyValuePair<string, string>("WEBSITE_INSTANCE_ID", "id"),
                })
                .Build();

            var provider = new JavaWorkerProvider();
            var args = new ArgumentsDescription();
            var result = provider.TryConfigureArguments(args, config, new TestLogger("test"));

            Assert.True(result);
            var exePath = Path.GetFullPath("d:/java/zulu8.23.0.3-jdk8.0.144-win_x64/bin/java");
            Assert.Equal(exePath, args.ExecutablePath);
        }
    }
}
