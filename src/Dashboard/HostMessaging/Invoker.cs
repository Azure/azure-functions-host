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
            FunctionInstanceSnapshot snapshot = CreateSnapshot(id, arguments, parentId, DateTimeOffset.UtcNow,
                functionId, function.FullName, function.ShortName);

            _logger.LogFunctionQueued(snapshot);
            _sender.Enqueue(queueName, message);

            return id;
        }


        public static FunctionInstanceSnapshot CreateSnapshot(Guid id, IDictionary<string, string> arguments,
            Guid? parentId, DateTimeOffset queueTime, string functionId, string functionFullName,
            string functionShortName)
        {
            return new FunctionInstanceSnapshot
            {
                Id = id,
                FunctionId = functionId,
                FunctionFullName = functionFullName,
                FunctionShortName = functionShortName,
                Arguments = CreateArguments(arguments),
                ParentId = parentId,
                Reason = parentId.HasValue ? "Replayed from Dashboard." : "Ran from Dashboard.",
                QueueTime = queueTime
            };
        }

        private static IDictionary<string, FunctionInstanceArgument> CreateArguments(IDictionary<string, string> values)
        {
            if (values == null)
            {
                return null;
            }

            Dictionary<string, FunctionInstanceArgument> arguments = new Dictionary<string, FunctionInstanceArgument>();

            foreach (KeyValuePair<string, string> value in values)
            {
                arguments.Add(value.Key, new FunctionInstanceArgument { Value = value.Value });
            }

            return arguments;
        }
    }
}
