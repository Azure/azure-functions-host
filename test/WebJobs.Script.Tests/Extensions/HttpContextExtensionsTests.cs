// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Azure.WebJobs.Script.Extensions;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Extensions;
using Microsoft.Extensions.Primitives;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Extensions
{
    public class HttpContextExtensionsTests
    {
        //TODO: Re-enable if needed for 3.0: https://github.com/Azure/azure-functions-host/issues/4865
        //[Fact]
        //public async Task SetOfflineResponseAsync_ReturnsCorrectResponse()
        //{
        //    string offlineFilePath = null;
        //    try
        //    {
        //        // create a test offline file
        //        var rootPath = Path.GetTempPath();
        //        offlineFilePath = TestHelpers.CreateOfflineFile();

        //        var sendFileFeatureMock = new Mock<IHttpSendFileFeature>();
        //        sendFileFeatureMock.Setup(s => s.SendFileAsync(offlineFilePath, 0, null, CancellationToken.None)).Returns(Task.FromResult<int>(0));

        //        // simulate an App Service request
        //        var vars = new Dictionary<string, string>
        //    {
        //        { EnvironmentSettingNames.AzureWebsiteInstanceId, "123" }
        //    };
        //        using (var env = new TestScopedEnvironmentVariable(vars))
        //        {
        //            // without header (thus an internal request)
        //            var context = new DefaultHttpContext();
        //            context.Features.Set(sendFileFeatureMock.Object);
        //            Assert.True(context.Request.IsAppServiceInternalRequest());
        //            await context.SetOfflineResponseAsync(rootPath);
        //            Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
        //            Assert.Equal(0, context.Response.Body.Length);
        //            sendFileFeatureMock.Verify(p => p.SendFileAsync(offlineFilePath, 0, null, CancellationToken.None), Times.Never);

        //            // with header (thus an external request)
        //            context = new DefaultHttpContext();
        //            context.Features.Set(sendFileFeatureMock.Object);
        //            context.Request.Headers.Add(ScriptConstants.AntaresLogIdHeaderName, new StringValues("456"));
        //            Assert.False(context.Request.IsAppServiceInternalRequest());
        //            await context.SetOfflineResponseAsync(rootPath);
        //            Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
        //            Assert.Equal("text/html", context.Response.Headers["Content-Type"]);
        //            sendFileFeatureMock.Verify(p => p.SendFileAsync(offlineFilePath, 0, null, CancellationToken.None), Times.Once);
        //        }
        //    }
        //    finally
        //    {
        //        TestHelpers.DeleteTestFile(offlineFilePath);
        //    }
        //}
    }
}
