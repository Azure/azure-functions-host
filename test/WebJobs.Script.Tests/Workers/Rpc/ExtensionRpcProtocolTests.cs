// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Workers.Rpc
{
    public class ExtensionRpcProtocolTests
    {
        [Fact]
        public void ExtensionTraffic_UsesDedicatedService()
        {
            var method = Assert.Single(
                ExtensionRpc.Descriptor.Methods,
                method => string.Equals(method.Name, "EventStream", StringComparison.Ordinal));

            Assert.Equal(ExtensionRpcMessage.Descriptor, method.InputType);
        }

        [Fact]
        public void ExtensionCallStart_RoundTripsCorrelationMetadataAndTimeout()
        {
            ExtensionRpcMessage message = new()
            {
                SessionId = "session-1",
                ShardId = "shard-1",
                CallId = "call-1",
                Start = new ExtensionRpcStart
                {
                    Method = "/azure.functions.extensions.Test/Invoke",
                    Timeout = Duration.FromTimeSpan(TimeSpan.FromSeconds(30)),
                    Metadata =
                    {
                        new ExtensionRpcMetadataEntry
                        {
                            Key = "trace-bin",
                            Value = ByteString.CopyFrom([0x01, 0x02, 0x03]),
                        },
                    },
                },
            };

            ExtensionRpcMessage parsed = ExtensionRpcMessage.Parser.ParseFrom(message.ToByteArray());

            Assert.Equal("session-1", parsed.SessionId);
            Assert.Equal("shard-1", parsed.ShardId);
            Assert.Equal("call-1", parsed.CallId);
            Assert.Equal(ExtensionRpcMessage.ContentOneofCase.Start, parsed.ContentCase);
            Assert.Equal("/azure.functions.extensions.Test/Invoke", parsed.Start.Method);
            Assert.Equal(TimeSpan.FromSeconds(30), parsed.Start.Timeout.ToTimeSpan());
            Assert.Equal(ByteString.CopyFrom([0x01, 0x02, 0x03]), parsed.Start.Metadata[0].Value);
        }

        [Fact]
        public void ExtensionCallCompletion_RoundTripsStatusAndTrailers()
        {
            ExtensionRpcMessage message = new()
            {
                SessionId = "session-1",
                ShardId = "shard-1",
                CallId = "call-1",
                Complete = new ExtensionRpcComplete
                {
                    Status = ExtensionRpcStatus.Unavailable,
                    Detail = "endpoint unavailable",
                    Trailers =
                    {
                        new ExtensionRpcMetadataEntry
                        {
                            Key = "retry-after",
                            Value = ByteString.CopyFromUtf8("1"),
                        },
                    },
                },
            };

            ExtensionRpcMessage parsed = ExtensionRpcMessage.Parser.ParseFrom(message.ToByteArray());

            Assert.Equal(ExtensionRpcStatus.Unavailable, parsed.Complete.Status);
            Assert.Equal("endpoint unavailable", parsed.Complete.Detail);
            Assert.Equal("1", parsed.Complete.Trailers[0].Value.ToStringUtf8());
        }

        [Fact]
        public void ExtensionFlowControl_RoundTripsPerCallCredit()
        {
            ExtensionRpcMessage message = new()
            {
                SessionId = "session-1",
                ShardId = "shard-1",
                CallId = "call-2",
                WindowUpdate = new ExtensionRpcWindowUpdate { ByteCount = 64 * 1024 },
            };

            ExtensionRpcMessage parsed = ExtensionRpcMessage.Parser.ParseFrom(message.ToByteArray());

            Assert.Equal("call-2", parsed.CallId);
            Assert.Equal((ulong)(64 * 1024), parsed.WindowUpdate.ByteCount);
        }

        [Fact]
        public void ExtensionData_RoundTripsMessageLength()
        {
            ExtensionRpcMessage message = new()
            {
                SessionId = "session-1",
                ShardId = "shard-1",
                CallId = "call-1",
                Data = new ExtensionRpcData
                {
                    MessageId = 2,
                    Offset = 64,
                    Payload = ByteString.CopyFrom([0x01, 0x02]),
                    EndOfMessage = true,
                    Compressed = false,
                    MessageLength = 66,
                },
            };

            ExtensionRpcMessage parsed = ExtensionRpcMessage.Parser.ParseFrom(message.ToByteArray());

            Assert.Equal((ulong)66, parsed.Data.MessageLength);
        }
    }
}
