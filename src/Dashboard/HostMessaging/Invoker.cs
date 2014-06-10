using System;
using System.Collections.Generic;
using Dashboard.Data;
using Microsoft.Azure.Jobs.Protocols;

namespace Dashboard.HostMessaging
{
    public class Invoker : IInvoker
    {
        private readonly IHostMessageSender _sender;
        private readonly IFunctionQueuedLogger _logger;

        public Invoker(IHostMessageSender sender, IFunctionQueuedLogger logger)
        {
            _sender = sender;
            _logger = logger;
        }

        public Guid TriggerAndOverride(string queueName, FunctionSnapshot function,
            IDictionary<string, string> arguments, Guid? parentId, ExecutionReason reason)
        {
            Guid id = Guid.NewGuid();

            CallAndOverrideMessage message = new CallAndOverrideMessage
            {
                Id = id,
                FunctionId = function.HostFunctionId,
                Arguments = arguments,
                ParentId = parentId,
                Reason = reason
            };

            FunctionStartedMessage queuedMessage = CreateFunctionStartedMessage(function, message);
            _logger.LogFunctionQueued(queuedMessage);

            _sender.Enqueue(queueName, message);

            return id;
        }

        private static FunctionStartedMessage CreateFunctionStartedMessage(FunctionSnapshot function,
            CallAndOverrideMessage message)
        {
            return new FunctionStartedMessage
            {
                FunctionInstanceId = message.Id,
                Function = new FunctionDescriptor
                {
                    Id = message.FunctionId,
                    FullName = function.FullName,
                    ShortName = function.ShortName
                },
                Arguments = message.Arguments,
                ParentId = message.ParentId,
                Reason = message.Reason,
                StartTime = DateTimeOffset.UtcNow
            };
        }
    }
}