// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Colors.Net;
using Microsoft.Azure.WebJobs.Host;
using NSubstitute;
using WebJobs.Script.Cli.Arm;
using WebJobs.Script.Cli.Arm.Models;
using WebJobs.Script.Cli.Extensions;
using WebJobs.Script.Cli.Interfaces;
using WebJobs.Script.Cli.Verbs;
using Xunit;

namespace WebJobs.Script.Cli.Tests.VerbsTests
{
    internal class NewVerbTests
    {
        public static IEnumerable<object[]> NewFunctionTestData
        {
            get
            {
                var subscription = new Subscription(Path.GetRandomFileName(), Path.GetRandomFileName());
                var functionApp = new Site(subscription.SubscriptionId, Path.GetRandomFileName(), Path.GetRandomFileName());
                yield return new object[] { Enumerable.Empty<Subscription>(), functionApp, -1, true };
                yield return new object[] { Enumerable.Repeat(subscription, 1), functionApp, -1, false };
                yield return new object[] { Enumerable.Repeat(subscription, 2), functionApp, -1, true };

                yield return new object[] { Enumerable.Repeat(subscription, 2), functionApp, -1, true };
                yield return new object[] { Enumerable.Repeat(subscription, 2), functionApp, -1, true };
            }
        }

        [Theory]
        [MemberData(nameof(NewFunctionTestData))]
        public async Task NewFunctionAppTest(IEnumerable<Subscription> subscriptions, Site functionApp, int selectedSub, bool error)
        {
            // Setup
            var armManager = Substitute.For<IArmManager>();
            var tipsManager = Substitute.For<ITipsManager>();
            var stderr = Substitute.For<IConsoleWriter>();
            ColoredConsole.Error = stderr;

            armManager.GetSubscriptionsAsync()
                .Returns(subscriptions);

            armManager.CreateFunctionAppAsync(Arg.Any<Subscription>(), Arg.Any<string>(), Arg.Any<string>())
                .Returns(new Site(subscriptions.Select(s => s.SubscriptionId).FirstOrDefault(), string.Empty, functionApp.SiteName));

            // Test
            var newVerb = new NewVerb(armManager, tipsManager)
            {
                NewOption = Common.Newable.FunctionApp,
                FunctionAppName = functionApp.SiteName,
            };

            await newVerb.RunAsync();

            // Assert
            if (error)
            {
                stderr
                    .Received()
                    .WriteLine(Arg.Is<TraceEvent>(t => t.Message == "Can't determine subscription Id, please add -s/--subscription <SubId>"));
            }
            else
            {
                Expression<Predicate<Subscription>> verifySub = s => s.SubscriptionId == subscriptions.First().SubscriptionId;
                Expression<Predicate<string>> verifyFunctionAppName = n => n == functionApp.SiteName;
                armManager
                    .Received()
                    .CreateFunctionAppAsync(Arg.Is(verifySub), Arg.Is(verifyFunctionAppName), Arg.Any<string>())
                    .Ignore();
            }
        }
    }
}
