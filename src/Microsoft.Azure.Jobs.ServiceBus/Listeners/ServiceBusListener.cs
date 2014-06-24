using System;
using System.Reflection;
using System.Threading;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Executors;
using Microsoft.Azure.Jobs.Host.Listeners;
using Microsoft.Azure.Jobs.Host.Protocols;
using Microsoft.Azure.Jobs.Host.Triggers;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.Jobs.ServiceBus.Listeners
{
    internal sealed class ServiceBusListener : IListener
    {
        private readonly MessagingFactory _factory;
        private readonly string _entityPath;
        private readonly ITriggeredFunctionBinding<BrokeredMessage> _functionBinding;
        private readonly FunctionDescriptor _functionDescriptor;
        private readonly MethodInfo _method;
        private readonly IFunctionExecutor _executor;
        private readonly RuntimeBindingProviderContext _context;

        private MessageReceiver _receiver;
        private CancellationTokenRegistration _cancellationRegistration;
        private bool _disposed;

        public ServiceBusListener(MessagingFactory factory, string entityPath,
            ITriggeredFunctionBinding<BrokeredMessage> functionBinding, FunctionDescriptor functionDescriptor,
            MethodInfo method, IFunctionExecutor executor, RuntimeBindingProviderContext context)
        {
            _factory = factory;
            _entityPath = entityPath;
            _functionBinding = functionBinding;
            _functionDescriptor = functionDescriptor;
            _method = method;
            _executor = executor;
            _context = context;
        }

        public void Start()
        {
            ThrowIfDisposed();

            if (_receiver != null)
            {
                throw new InvalidOperationException("The listener has already been started.");
            }

            CancellationToken cancellationToken = _context.CancellationToken;

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            _receiver = _factory.CreateMessageReceiver(_entityPath);

            if (cancellationToken.IsCancellationRequested)
            {
                _receiver.Abort();
                return;
            }

            _cancellationRegistration = cancellationToken.Register(_receiver.Close);
            _receiver.OnMessage(ProcessMessage, new OnMessageOptions());
        }

        public void Stop()
        {
            ThrowIfDisposed();

            if (_receiver == null)
            {
                throw new InvalidOperationException("The listener has not yet been started or has already been stopped.");
            }

            _cancellationRegistration.Dispose();
            _receiver.Close();
            _receiver = null;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _cancellationRegistration.Dispose();

                if (_receiver != null)
                {
                    _receiver.Abort();
                    _receiver = null;
                }

                _disposed = true;
            }
        }

        private void ProcessMessage(BrokeredMessage message)
        {
            CancellationToken cancellationToken = _context.CancellationToken;

            if (cancellationToken.IsCancellationRequested)
            {
                message.Abandon();
                return;
            }

            Guid functionInstanceId = Guid.NewGuid();
            IBindCommand bindCommand = new TriggerBindCommand<BrokeredMessage>(_functionBinding, _context, functionInstanceId, message);
            Guid? parentId = ServiceBusCausalityHelper.GetOwner(message);
            IFunctionInstance instance = new FunctionInstance(functionInstanceId, parentId,
                ExecutionReason.AutomaticTrigger, bindCommand, _functionDescriptor, _method);

            if (!_executor.Execute(instance))
            {
                message.Abandon();
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(null);
            }
        }
    }
}
