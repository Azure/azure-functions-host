// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Google.Protobuf;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Azure.Functions.Rpc.Client.Tests;

public sealed class WorkerHostJsonConfigurationTests
{
    public static IEnumerable<object?[]> InvalidDeliveries()
    {
        foreach ((HostJsonConfiguration.Types.Status status, string reason) in new[]
        {
            (HostJsonConfiguration.Types.Status.Unspecified, "The worker host.json delivery status is unspecified or unsupported."),
            ((HostJsonConfiguration.Types.Status)5, "The worker host.json delivery status is unspecified or unsupported."),
            (HostJsonConfiguration.Types.Status.ReadError, "The worker could not read host.json."),
            (HostJsonConfiguration.Types.Status.TooLarge, "The worker host.json exceeds the 65,536-byte limit."),
            ((HostJsonConfiguration.Types.Status)100, "The worker host.json delivery status is unspecified or unsupported."),
        })
        {
            yield return [new HostJsonConfiguration { Status = status }, reason];
        }

        foreach (HostJsonConfiguration.Types.Status status in Enum.GetValues<HostJsonConfiguration.Types.Status>()
            .Append((HostJsonConfiguration.Types.Status)5)
            .Where(status => status != HostJsonConfiguration.Types.Status.Available))
        {
            yield return
            [
                new HostJsonConfiguration { Status = status, Content = ByteString.CopyFromUtf8("customer-secret") },
                "The worker host.json delivery has content inconsistent with its status.",
            ];
        }

        foreach (string content in new[]
        {
            string.Empty,
            " ",
            "[]",
            "null",
            "{customer-secret",
            """{"version":"customer-secret"}""",
            """{"version":"2.0","configurationProfile":"customer-secret"}""",
        })
        {
            yield return
            [
                Available(ByteString.CopyFromUtf8(content)),
                "The worker host.json is not valid JSON with a supported schema version and configuration profile.",
            ];
        }

        yield return
        [
            Available(ByteString.CopyFromUtf8("customer-secret".PadRight(65_537, 'x'))),
            "The worker host.json exceeds the 65,536-byte limit.",
        ];
        foreach (byte[] invalidEncoding in new byte[][]
        {
            [0xC3, 0x28],
            [0xFF, 0xFE, 0x00, 0xD8],
            [0xFE, 0xFF, 0xD8, 0x00],
            [0xFF, 0xFE, 0x00, 0x00, 0x00, 0x00, 0x11, 0x00],
            [0x00, 0x00, 0xFE, 0xFF, 0x00, 0x11, 0x00, 0x00],
        })
        {
            yield return
            [
                Available(ByteString.CopyFrom(invalidEncoding)),
                "The worker host.json contains invalid encoded text.",
            ];
        }
    }

    [Theory]
    [MemberData(nameof(InvalidDeliveries))]
    public void Validate_InvalidDeliveryRejectsWithBoundedContentFreeError(HostJsonConfiguration delivery, string expectedReason)
    {
        WorkerLinkException exception = Assert.Throws<WorkerLinkException>(() => CreateValidator().Validate(delivery));

        Assert.Equal(WorkerLinkFailureReason.Unavailable, exception.Reason);
        Assert.Equal(expectedReason, exception.Message);
        Assert.Null(exception.InnerException);
        Assert.InRange(exception.Message.Length, 1, 160);
        Assert.DoesNotContain("customer-secret", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_AbsentEnvelopeSelectsLegacyWithoutReadingConfiguration()
    {
        var configuration = new Mock<IConfiguration>(MockBehavior.Strict);
        var applicationOptions = new Mock<IOptionsMonitor<ScriptApplicationHostOptions>>(MockBehavior.Strict);
        var logger = new Mock<ILogger<WorkerHostJsonConfiguration>>(MockBehavior.Strict);
        WorkerHostJsonConfiguration validator = new(configuration.Object, applicationOptions.Object, logger.Object);

        Assert.Null(validator.Validate(null));

        configuration.VerifyNoOtherCalls();
        applicationOptions.VerifyNoOtherCalls();
        logger.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("fr-FR")]
    [InlineData("de-DE")]
    [InlineData("ar-SA")]
    public void Validate_SizeLimitReasonIsCultureIndependent(string culture)
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            HostJsonConfiguration[] deliveries =
            [
                new() { Status = HostJsonConfiguration.Types.Status.TooLarge },
                Available(ByteString.CopyFrom(new byte[WorkerHostJsonConfiguration.MaximumContentLength + 1])),
            ];

            foreach (HostJsonConfiguration delivery in deliveries)
            {
                WorkerLinkException exception = Assert.Throws<WorkerLinkException>(() => CreateValidator().Validate(delivery));
                Assert.Equal("The worker host.json exceeds the 65,536-byte limit.", exception.Message);
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData(65001, false)]
    [InlineData(65001, true)]
    [InlineData(1200, true)]
    [InlineData(1201, true)]
    [InlineData(12000, true)]
    [InlineData(12001, true)]
    public void Validate_AcceptsFileEncodingsWithBomInsideExactRawByteLimit(int codePage, bool withBom)
    {
        Encoding encoding = Encoding.GetEncoding(codePage);
        byte[] preamble = withBom ? encoding.GetPreamble() : [];
        byte[] document = encoding.GetBytes("{\"version\":\"2.0\",\"custom\":\"caf\u00e9 \ud83d\ude80\"}");
        int paddingCharacters = (65_536 - preamble.Length - document.Length) / encoding.GetByteCount(" ");
        byte[] content = [.. preamble, .. document, .. encoding.GetBytes(new string(' ', paddingCharacters))];
        Assert.Equal(65_536, content.Length);

        HostJsonConfigurationSnapshot snapshot = CreateValidator().Validate(Available(ByteString.CopyFrom(content)));
        IConfigurationRoot configuration = new ConfigurationBuilder().Add(snapshot).Build();

        Assert.Equal("caf\u00e9 \ud83d\ude80", configuration["AzureFunctionsJobHost:custom"]);
        byte[] oversized = [.. content, 0x20];
        WorkerLinkException exception = Assert.Throws<WorkerLinkException>(() =>
            CreateValidator().Validate(Available(ByteString.CopyFrom(oversized))));
        Assert.Equal("The worker host.json exceeds the 65,536-byte limit.", exception.Message);
    }

    [Fact]
    public void Validate_MissingAndEmptyObjectUseDifferentExistingDefaults()
    {
        WorkerHostJsonConfiguration validator = CreateValidator(readOnly: false);
        IConfigurationRoot missing = new ConfigurationBuilder().Add(validator.Validate(
            new HostJsonConfiguration { Status = HostJsonConfiguration.Types.Status.Missing })).Build();
        IConfigurationRoot emptyObject = new ConfigurationBuilder().Add(validator.Validate(
            Available(ByteString.CopyFromUtf8("{}")))).Build();

        Assert.Equal("2.0", missing["AzureFunctionsJobHost:version"]);
        Assert.Equal("True", missing["AzureFunctionsJobHost:isDefaultHostConfig"]);
        Assert.Equal(ScriptConstants.DefaultExtensionBundleId, missing["AzureFunctionsJobHost:extensionBundle:id"]);
        Assert.Equal("True", emptyObject["AzureFunctionsJobHost:isDefaultHostConfig"]);
        Assert.Null(emptyObject["AzureFunctionsJobHost:extensionBundle:id"]);
    }

    [Fact]
    public void Validate_ReadsBootstrapProfileAndDefaultsWithoutWritingRootConfiguration()
    {
        var configuration = new Mock<IConfiguration>(MockBehavior.Strict);
        configuration.SetupGet(value => value["AzureFunctionsJobHost:configurationProfile"]).Returns("mcp-custom-handler");
        configuration.SetupGet(value => value["FUNCTIONS_WORKER_RUNTIME"]).Returns("powershell");
        configuration.SetupGet(value => value["APP_KIND"]).Returns("workflowApp");
        var applicationOptions = new Mock<IOptionsMonitor<ScriptApplicationHostOptions>>();
        applicationOptions.SetupGet(options => options.CurrentValue).Returns(new ScriptApplicationHostOptions());
        WorkerHostJsonConfiguration validator = new(
            configuration.Object, applicationOptions.Object,
            NullLogger<WorkerHostJsonConfiguration>.Instance);

        IConfigurationRoot result = new ConfigurationBuilder().Add(validator.Validate(
            new HostJsonConfiguration { Status = HostJsonConfiguration.Types.Status.Missing })).Build();

        Assert.Equal("mcp-custom-handler", result["AzureFunctionsJobHost:configurationProfile"]);
        Assert.Equal("true", result["AzureFunctionsJobHost:customHandler:enableProxyingHttpRequest"]);
        Assert.Equal("True", result["AzureFunctionsJobHost:managedDependency:Enabled"]);
        Assert.Equal(ScriptConstants.WorkFlowExtensionBundleId, result["AzureFunctionsJobHost:extensionBundle:id"]);
        configuration.VerifySet(value => value[It.IsAny<string>()] = It.IsAny<string?>(), Times.Never);
    }

    private static HostJsonConfiguration Available(ByteString content)
        => new() { Status = HostJsonConfiguration.Types.Status.Available, Content = content };

    private static WorkerHostJsonConfiguration CreateValidator(bool readOnly = true)
    {
        var applicationOptions = new Mock<IOptionsMonitor<ScriptApplicationHostOptions>>();
        applicationOptions.SetupGet(options => options.CurrentValue)
            .Returns(new ScriptApplicationHostOptions { IsFileSystemReadOnly = readOnly });

        return new(
            new ConfigurationBuilder().Build(),
            applicationOptions.Object,
            NullLogger<WorkerHostJsonConfiguration>.Instance);
    }
}
