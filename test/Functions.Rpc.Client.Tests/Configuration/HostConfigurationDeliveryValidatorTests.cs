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

public sealed class HostConfigurationDeliveryValidatorTests
{
    public static IEnumerable<object?[]> InvalidDeliveries()
    {
        foreach ((HostConfiguration.Types.Status status, string reason) in new[]
        {
            (HostConfiguration.Types.Status.Unspecified, "The Host configuration delivery status is unspecified or unsupported."),
            ((HostConfiguration.Types.Status)5, "The Host configuration delivery status is unspecified or unsupported."),
            (HostConfiguration.Types.Status.ReadError, "The worker could not read host.json."),
            (HostConfiguration.Types.Status.TooLarge, "The Host configuration exceeds the 65,536-byte limit."),
            ((HostConfiguration.Types.Status)100, "The Host configuration delivery status is unspecified or unsupported."),
        })
        {
            yield return [new HostConfiguration { Status = status }, reason];
        }

        foreach (HostConfiguration.Types.Status status in Enum.GetValues<HostConfiguration.Types.Status>()
            .Append((HostConfiguration.Types.Status)5)
            .Where(status => status != HostConfiguration.Types.Status.Available))
        {
            yield return
            [
                new HostConfiguration { Status = status, Content = ByteString.CopyFromUtf8("customer-secret") },
                "The Host configuration delivery has content inconsistent with its status.",
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
                "The Host configuration is not valid JSON with a supported schema version and configuration profile.",
            ];
        }

        yield return
        [
            Available(ByteString.CopyFromUtf8("customer-secret".PadRight(65_537, 'x'))),
            "The Host configuration exceeds the 65,536-byte limit.",
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
                "The Host configuration contains invalid encoded text.",
            ];
        }
    }

    [Theory]
    [MemberData(nameof(InvalidDeliveries))]
    public void Validate_InvalidDeliveryRejectsWithBoundedContentFreeError(HostConfiguration delivery, string expectedReason)
    {
        WorkerLinkException exception = Assert.Throws<WorkerLinkException>(() => CreateValidator().Validate(delivery));

        Assert.Equal(WorkerLinkFailureReason.Unavailable, exception.Reason);
        Assert.Equal(expectedReason, exception.Message);
        Assert.Null(exception.InnerException);
        Assert.InRange(exception.Message.Length, 1, 160);
        Assert.DoesNotContain("customer-secret", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_AbsentEnvelopeSelectsFileBasedLoadingWithoutReadingConfiguration()
    {
        var configuration = new Mock<IConfiguration>(MockBehavior.Strict);
        var applicationOptions = new Mock<IOptionsMonitor<ScriptApplicationHostOptions>>(MockBehavior.Strict);
        var logger = new Mock<ILogger<HostConfigurationDeliveryValidator>>(MockBehavior.Strict);
        HostConfigurationDeliveryValidator validator = new(configuration.Object, applicationOptions.Object, logger.Object);

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
            HostConfiguration[] deliveries =
            [
                new() { Status = HostConfiguration.Types.Status.TooLarge },
                Available(ByteString.CopyFrom(new byte[HostConfigurationDeliveryValidator.MaximumContentLength + 1])),
            ];

            foreach (HostConfiguration delivery in deliveries)
            {
                WorkerLinkException exception = Assert.Throws<WorkerLinkException>(() => CreateValidator().Validate(delivery));
                Assert.Equal("The Host configuration exceeds the 65,536-byte limit.", exception.Message);
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

        HostConfigurationSnapshot snapshot = CreateValidator().Validate(Available(ByteString.CopyFrom(content)));
        IConfigurationRoot configuration = new ConfigurationBuilder().Add(snapshot).Build();

        Assert.Equal("caf\u00e9 \ud83d\ude80", configuration["AzureFunctionsJobHost:custom"]);
        byte[] oversized = [.. content, 0x20];
        WorkerLinkException exception = Assert.Throws<WorkerLinkException>(() =>
            CreateValidator().Validate(Available(ByteString.CopyFrom(oversized))));
        Assert.Equal("The Host configuration exceeds the 65,536-byte limit.", exception.Message);
    }

    [Fact]
    public void Validate_MissingAndEmptyObjectUseDifferentExistingDefaults()
    {
        HostConfigurationDeliveryValidator validator = CreateValidator(readOnly: false);
        IConfigurationRoot missing = new ConfigurationBuilder().Add(validator.Validate(
            new HostConfiguration { Status = HostConfiguration.Types.Status.Missing })).Build();
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
        HostConfigurationDeliveryValidator validator = new(
            configuration.Object, applicationOptions.Object,
            NullLogger<HostConfigurationDeliveryValidator>.Instance);

        IConfigurationRoot result = new ConfigurationBuilder().Add(validator.Validate(
            new HostConfiguration { Status = HostConfiguration.Types.Status.Missing })).Build();

        Assert.Equal("mcp-custom-handler", result["AzureFunctionsJobHost:configurationProfile"]);
        Assert.Equal("true", result["AzureFunctionsJobHost:customHandler:enableProxyingHttpRequest"]);
        Assert.Equal("True", result["AzureFunctionsJobHost:managedDependency:Enabled"]);
        Assert.Equal(ScriptConstants.WorkFlowExtensionBundleId, result["AzureFunctionsJobHost:extensionBundle:id"]);
        configuration.VerifySet(value => value[It.IsAny<string>()] = It.IsAny<string?>(), Times.Never);
    }

    private static HostConfiguration Available(ByteString content)
        => new() { Status = HostConfiguration.Types.Status.Available, Content = content };

    private static HostConfigurationDeliveryValidator CreateValidator(bool readOnly = true)
    {
        var applicationOptions = new Mock<IOptionsMonitor<ScriptApplicationHostOptions>>();
        applicationOptions.SetupGet(options => options.CurrentValue)
            .Returns(new ScriptApplicationHostOptions { IsFileSystemReadOnly = readOnly });

        return new(
            new ConfigurationBuilder().Build(),
            applicationOptions.Object,
            NullLogger<HostConfigurationDeliveryValidator>.Instance);
    }
}
