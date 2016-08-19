// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Colors.Net;
using NSubstitute;
using WebJobs.Script.Cli.Verbs;
using Xunit;

namespace WebJobs.Script.Cli.Tests.VerbsTests
{
    public class BaseVerbTests
    {
        [Theory]
        [InlineData("Exception Message", true)]
        [InlineData("Exception Message", false)]
        public async Task OnErrorAsyncTest(string exceptionMessage, bool cliDev)
        {
            // Setup
            var verb = new HelpVerb(null, null)
            {
                CliDev = cliDev
            };

            var stdout = Substitute.For<IConsoleWriter>();
            var stderr = Substitute.For<IConsoleWriter>();
            ColoredConsole.Out = stdout;
            ColoredConsole.Error = stderr;

            // Test
            await verb.OnErrorAsync(new Exception(exceptionMessage));

            // Assert
            if (!cliDev)
            {
                stdout
                    .Received()
                    .WriteLine(Arg.Is<object>(e => e.ToString().Contains("--cli-dev")));
            }

            stderr
                .Received()
                .WriteLine(Arg.Is<object>(e => e.ToString().Contains(exceptionMessage)));
        }
    }
}
