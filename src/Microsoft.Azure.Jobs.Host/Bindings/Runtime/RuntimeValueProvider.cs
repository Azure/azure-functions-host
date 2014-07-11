using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Microsoft.Azure.Jobs.Host.Protocols;

namespace Microsoft.Azure.Jobs.Host.Bindings.Runtime
{
    internal sealed class RuntimeValueProvider : IValueBinder, IWatchable, IDisposable, IBinder
    {
        private readonly IAttributeBindingSource _bindingSource;
        private readonly IList<IValueBinder> _binders = new List<IValueBinder>();
        private readonly RuntimeBindingWatcher _watcher = new RuntimeBindingWatcher();
        private readonly CollectingDisposable _disposable = new CollectingDisposable();

        private bool _disposed;

        public RuntimeValueProvider(IAttributeBindingSource bindingSource)
        {
            _bindingSource = bindingSource;
        }

        public CancellationToken CancellationToken
        {
            get { return _bindingSource.CancellationToken; }
        }

        public Type Type
        {
            get { return typeof(IBinder); }
        }

        public IWatcher Watcher
        {
            get { return _watcher; }
        }

        public object GetValue()
        {
            return this;
        }

        public void SetValue(object value)
        {
            foreach (IValueBinder binder in _binders)
            {
                // RuntimeBinding can only be uses for non-Out parameters, and their binders ignore this argument.
                binder.SetValue(value: null);
            }
        }

        public string ToInvokeString()
        {
            return null;
        }

        public TValue Bind<TValue>(Attribute attribute)
        {
            IBinding binding = _bindingSource.Bind<TValue>(attribute);

            if (binding == null)
            {
                throw new InvalidOperationException("No binding found for attribute '" + attribute.GetType() + "'.");
            }

            IValueProvider provider = binding.Bind(_bindingSource.BindingContext);

            if (provider == null)
            {
                return default(TValue);
            }

            Debug.Assert(provider.Type == typeof(TValue));

            ParameterDescriptor parameterDesciptor = binding.ToParameterDescriptor();
            parameterDesciptor.Name = null; // Remove the dummy name "?" used for runtime binding.

            string value = provider.ToInvokeString();

            IWatchable watchable = provider as IWatchable;

            // Add even if watchable is null to show parameter descriptor in status.
            _watcher.Add(parameterDesciptor, value, watchable);

            IValueBinder binder = provider as IValueBinder;

            if (binder != null)
            {
                _binders.Add(binder);
            }

            IDisposable disposableProvider = provider as IDisposable;

            if (disposableProvider != null)
            {
                _disposable.Add(disposableProvider);
            }

            return (TValue)provider.GetValue();
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposable.Dispose();
                _disposed = true;
            }
        }
    }
}
