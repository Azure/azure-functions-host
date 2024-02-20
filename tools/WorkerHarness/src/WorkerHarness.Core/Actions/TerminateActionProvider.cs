// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.Functions.WorkerHarness.Grpc.Messages;
using Microsoft.Extensions.Logging;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using WorkerHarness.Core.GrpcService;
using WorkerHarness.Core.StreamingMessageService;

namespace WorkerHarness.Core.Actions
{
    public sealed class TerminateActionProvider : IActionProvider
    {
        public string Type => ActionTypes.Terminate;

        private readonly Channel<StreamingMessage> _outboundChannel;
        private readonly IStreamingMessageProvider _streamingMessageProvider;
        private readonly ILoggerFactory _loggerFactory;

        internal static string MissingGracePeriodInSeconds = "The terminate action is missing a gracePeriodInSeconds property";
        internal static string GracePeriodIsNotInteger = "The value of the gracePeriodInSeconds must be an integer. It's currently not";
        internal static string GracePeriodIsNegative = "The value of the gracePeriodInSeconds must be non-negative. It's currently not";

        public TerminateActionProvider(GrpcServiceChannel channel, 
            IStreamingMessageProvider streamingMessageProvider, ILoggerFactory loggerFactory)
        {
            _outboundChannel = channel.OutboundChannel;
            _streamingMessageProvider = streamingMessageProvider;
            _loggerFactory = loggerFactory;
        }

        public IAction Create(JsonNode actionNode)
        {
            try
            {
                TryGetGracePeriod(out int gracePeriodInSeconds, actionNode);

                return new TerminateAction(gracePeriodInSeconds, _outboundChannel,
                    _streamingMessageProvider, _loggerFactory.CreateLogger<TerminateAction>());
            }
            catch (ArgumentException ex)
            {
                throw ex;
            }
        }

        private static bool TryGetGracePeriod(out int gracePeriodInSeconds, JsonNode actionNode)
        {
            if (actionNode["gracePeriodInSeconds"] == null)
            {
                throw new ArgumentException(MissingGracePeriodInSeconds);
            }

            try
            {
                gracePeriodInSeconds = actionNode["gracePeriodInSeconds"]!.GetValue<int>();

                if (gracePeriodInSeconds < 0)
                {
                    throw new ArgumentOutOfRangeException(GracePeriodIsNegative);
                }
            }
            catch (FormatException ex)
            {
                throw new ArgumentException(GracePeriodIsNotInteger, ex);
            }
            catch (InvalidOperationException ex)
            {
                throw new ArgumentException(GracePeriodIsNotInteger, ex);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                throw ex;
            }

            return true;
        }
    }
}
