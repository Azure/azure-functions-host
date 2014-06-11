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

            string functionId = new FunctionIdentifier(function.QueueName, function.HostFunctionId).ToString();
            _logger.LogFunctionQueued(id, arguments, parentId, DateTimeOffset.UtcNow, functionId, function.FullName,
                function.ShortName);

            _sender.Enqueue(queueName, message);

            return id;
        }
    }
}
