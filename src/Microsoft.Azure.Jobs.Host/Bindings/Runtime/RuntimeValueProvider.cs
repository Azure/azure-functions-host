using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace Microsoft.Azure.Jobs.Host.Bindings.Runtime
{
    internal sealed class RuntimeValueProvider : IValueBinder, IWatchable, IDisposable, IBinder
    {
        private readonly IAttributeBinding _binding;
        private readonly IList<IValueBinder> _binders = new List<IValueBinder>();
        private readonly CompositeSelfWatch _watcher = new CompositeSelfWatch();
        private readonly CompositeDisposable _disposable = new CompositeDisposable();

        private bool _disposed;

        public RuntimeValueProvider(IAttributeBinding binding)
        {
            _binding = binding;
        }

        public CancellationToken CancellationToken
        {
            get { return _binding.CancellationToken; }
        }

        public Type Type
        {
            get { return typeof(IBinder); }
        }

        public ISelfWatch Watcher
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

        public T Bind<T>(Attribute attribute)
        {
            IValueProvider provider = _binding.Bind<T>(attribute);

            if (provider == null)
            {
                return default(T);
            }

            Debug.Assert(provider.Type == typeof(T));

            IWatchable watchable = provider as IWatchable;
            _watcher.Add(attribute.ToString(), watchable); // Add even if null to show name in status.

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

            return (T)provider.GetValue();
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
