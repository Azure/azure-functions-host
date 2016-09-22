// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Net;
using System.Net.Http;
using System.Text;
using ARMClient.Authentication;
using ARMClient.Library;
using NSubstitute;

namespace WebJobs.Script.Cli.Tests.ArmTests
{
    public static class AzureClientFactory
    {
        private static HttpResponseMessage ToResponse(string content, string mediaType)
        {
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(content, Encoding.UTF8, mediaType) };
        }

        public static IAzureClient GetAzureClient()
        {
            var client = Substitute.For<IAzureClient>();

            // Subscriptions
            client.HttpInvoke(Arg.Is<HttpMethod>(m => m == HttpMethod.Get), Arg.Is<Uri>(u => u == ArmData.MultipleSubscriptions.Uri))
                .Returns(ToResponse(ArmData.MultipleSubscriptions.Value, ArmData.MultipleSubscriptions.ContentType));

            return client;
        }

        public static IAuthHelper GetAuthHelper()
        {
            return Substitute.For<IAuthHelper>();
        }
    }
}
