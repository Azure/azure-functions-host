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
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Azure.Functions.Rpc.Client.Tests;

public sealed class WorkerHostConfigurationParserTests
{
    public static IEnumerable<object?[]> InvalidDeliveries()
    {
        foreach ((HostConfiguration.Types.Status status, string reason) in new[]
        {
            (HostConfiguration.Types.Status.Unspecified, "The Host configuration delivery status is unspecified or unsupported."),
            ((HostConfiguration.Types.Status)5, "The Host configuration delivery status is unspecified or unsupported."),
            (HostConfiguration.Types.Status.Error, "The worker could not produce Host configuration."),
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
            """{"configurationProfile":"customer-secret"}""",
            """{"configurationProfile":{"customer-secret":true}}""",
            """{"functionTimeout":undefined,"custom":"customer-secret"}""",
            """{"extensions":{"custom":{"customer-secret":[undefined]}}}""",
            """{"extensions":{"custom":{"customer-secret":NaN}}}""",
            """{"extensions":{"custom":{"customer-secret":Infinity}}}""",
            """{"extensions":{"custom":{"customer-secret":-Infinity}}}""",
            """{'custom':'customer-secret'}""",
            """{custom:"customer-secret"}""",
            """{/* customer-secret */"custom":true}""",
            """{"custom":"customer-secret",}""",
            """{} {"custom":"customer-secret"}""",
        })
        {
            yield return
            [
                Available(ByteString.CopyFromUtf8(content)),
                "The Host configuration must be a JSON object with a supported configuration profile.",
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
            [0xF0, 0x9F, 0x98],
            [0xED, 0xA0, 0x80],
            [0xC0, 0xAF],
            [0xF4, 0x90, 0x80, 0x80],
            [0xFF, 0xFE, 0x00, 0xD8],
            [0xFE, 0xFF, 0xD8, 0x00],
            [0xFF, 0xFE, 0x00, 0x00, 0x00, 0x00, 0x11, 0x00],
            [0x00, 0x00, 0xFE, 0xFF, 0x00, 0x11, 0x00, 0x00],
        })
        {
            yield return
            [
                Available(ByteString.CopyFrom(invalidEncoding)),
                "The Host configuration contains invalid UTF-8 text.",
            ];
        }

        yield return
        [
            Available(ByteString.CopyFrom([0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("{}")])),
            "The Host configuration must be UTF-8 JSON without a byte order mark.",
        ];
    }

    [Theory]
    [MemberData(nameof(InvalidDeliveries))]
    public void Parse_InvalidDeliveryRejectsWithBoundedContentFreeError(HostConfiguration delivery, string expectedReason)
    {
        HostConfigurationException exception = Assert.Throws<HostConfigurationException>(() => CreateParser().Parse(delivery));

        Assert.Equal(expectedReason, exception.Message);
        Assert.Null(exception.InnerException);
        Assert.InRange(exception.Message.Length, 1, 160);
        Assert.DoesNotContain("customer-secret", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_AbsentEnvelopeReturnsNullWithoutReadingConfiguration()
    {
        var configuration = new Mock<IConfiguration>(MockBehavior.Strict);
        var logger = new Mock<ILogger<WorkerHostConfigurationParser>>(MockBehavior.Strict);
        WorkerHostConfigurationParser parser = new(configuration.Object, logger.Object);

        Assert.Null(parser.Parse(null));

        configuration.VerifyNoOtherCalls();
        logger.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("fr-FR")]
    [InlineData("de-DE")]
    [InlineData("ar-SA")]
    public void Parse_SizeLimitReasonIsCultureIndependent(string culture)
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            HostConfiguration[] deliveries =
            [
                new() { Status = HostConfiguration.Types.Status.TooLarge },
                Available(ByteString.CopyFrom(new byte[WorkerHostConfigurationParser.MaximumContentLength + 1])),
            ];

            foreach (HostConfiguration delivery in deliveries)
            {
                HostConfigurationException exception = Assert.Throws<HostConfigurationException>(() => CreateParser().Parse(delivery));
                Assert.Equal("The Host configuration exceeds the 65,536-byte limit.", exception.Message);
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData(65_535)]
    [InlineData(65_536)]
    public void Parse_AcceptsUtf8PayloadUpToExactByteLimit(int byteCount)
    {
        byte[] document = Encoding.UTF8.GetBytes("{\"custom\":\"caf\u00e9 \ud83d\ude80\"}");
        byte[] content = [.. document, .. Encoding.UTF8.GetBytes(new string(' ', byteCount - document.Length))];
        Assert.Equal(byteCount, content.Length);

        IConfigurationSource source = CreateParser().Parse(Available(ByteString.CopyFrom(content)));
        IConfigurationRoot configuration = new ConfigurationBuilder().Add(source).Build();

        Assert.Equal("caf\u00e9 \ud83d\ude80", configuration["AzureFunctionsJobHost:custom"]);
    }

    [Fact]
    public void Parse_LimitCountsUtf8BytesNotCharacters()
    {
        string content = "{\"custom\":\"" + new string('\u00e9', 32_768) + "\"}";
        Assert.True(content.Length < WorkerHostConfigurationParser.MaximumContentLength);
        Assert.True(Encoding.UTF8.GetByteCount(content) > WorkerHostConfigurationParser.MaximumContentLength);

        HostConfigurationException exception = Assert.Throws<HostConfigurationException>(() =>
            CreateParser().Parse(Available(ByteString.CopyFromUtf8(content))));

        Assert.Equal("The Host configuration exceeds the 65,536-byte limit.", exception.Message);
    }

    [Theory]
    [InlineData(65001)]
    [InlineData(1200)]
    [InlineData(1201)]
    [InlineData(12000)]
    [InlineData(12001)]
    public void Parse_RejectsBomEncodedPayload(int codePage)
    {
        Encoding encoding = Encoding.GetEncoding(codePage);
        byte[] content = [.. encoding.GetPreamble(), .. encoding.GetBytes("{}")];

        HostConfigurationException exception = Assert.Throws<HostConfigurationException>(() =>
            CreateParser().Parse(Available(ByteString.CopyFrom(content))));

        Assert.Equal(codePage == 65001
            ? "The Host configuration must be UTF-8 JSON without a byte order mark."
            : "The Host configuration contains invalid UTF-8 text.", exception.Message);
        Assert.Null(exception.InnerException);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Parse_MissingAndEmptyObjectDoNotApplyFileDefaults(bool missing)
    {
        HostConfiguration delivery = missing
            ? new() { Status = HostConfiguration.Types.Status.Missing }
            : Available(ByteString.CopyFromUtf8("{}"));

        IConfigurationRoot result = new ConfigurationBuilder().Add(CreateParser().Parse(delivery)).Build();

        Assert.Equal("default", result["AzureFunctionsJobHost:configurationProfile"]);
        Assert.Null(result["AzureFunctionsJobHost:version"]);
        Assert.Null(result["AzureFunctionsJobHost:isDefaultHostConfig"]);
        Assert.Null(result["AzureFunctionsJobHost:extensionBundle:id"]);
        Assert.Null(result["AzureFunctionsJobHost:managedDependency:Enabled"]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Parse_ReadsOnlyBootstrapProfileWithoutWritingRootConfiguration(bool missing)
    {
        var configuration = new Mock<IConfiguration>(MockBehavior.Strict);
        configuration.SetupGet(value => value["AzureFunctionsJobHost:configurationProfile"]).Returns("mcp-custom-handler");
        WorkerHostConfigurationParser parser = new(configuration.Object, NullLogger<WorkerHostConfigurationParser>.Instance);
        HostConfiguration delivery = missing
            ? new() { Status = HostConfiguration.Types.Status.Missing }
            : Available(ByteString.CopyFromUtf8("""{"configurationProfile":"default"}"""));

        IConfigurationRoot result = new ConfigurationBuilder().Add(parser.Parse(delivery)).Build();

        Assert.Equal(missing ? "mcp-custom-handler" : "default", result["AzureFunctionsJobHost:configurationProfile"]);
        Assert.Equal("true", result["AzureFunctionsJobHost:customHandler:enableProxyingHttpRequest"]);
        Assert.Null(result["AzureFunctionsJobHost:managedDependency:Enabled"]);
        Assert.Null(result["AzureFunctionsJobHost:extensionBundle:id"]);
        configuration.VerifySet(value => value[It.IsAny<string>()] = It.IsAny<string?>(), Times.Never);
        configuration.VerifyGet(value => value["AzureFunctionsJobHost:configurationProfile"], Times.Once);
        configuration.VerifyNoOtherCalls();
    }

    private static HostConfiguration Available(ByteString content)
        => new() { Status = HostConfiguration.Types.Status.Available, Content = content };

    private static WorkerHostConfigurationParser CreateParser()
        => new(new ConfigurationBuilder().Build(), NullLogger<WorkerHostConfigurationParser>.Instance);
}
