using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using Microsoft.Azure.Jobs.Host.Protocols;

namespace Microsoft.Azure.Jobs.Host.Bindings
{
    internal class RuntimeBinding : IBinding
    {
        private IValueProvider Bind(IAttributeBinding binding, ArgumentBindingContext context)
        {
            return new RuntimeBindingValueProvider(binding);
        }

        public IValueProvider Bind(object value, ArgumentBindingContext context)
        {
            IAttributeBinding binding = value as IAttributeBinding;

            if (binding == null)
            {
                throw new InvalidOperationException("Unable to convert value to IAttributeBinding.");
            }

            return Bind(binding, context);
        }

        public IValueProvider Bind(BindingContext context)
        {
            return Bind(new AttributeBinding(context), context);
        }

        public ParameterDescriptor ToParameterDescriptor()
        {
            return new BinderParameterDescriptor();
        }

        private sealed class RuntimeBindingValueProvider : IValueBinder, IWatchable, IDisposable, IBinder
        {
            private readonly IAttributeBinding _binding;
            private readonly IList<IValueBinder> _binders = new List<IValueBinder>();
            private readonly CompositeSelfWatch _watcher = new CompositeSelfWatch();
            private readonly IList<IDisposable> _disposables = new List<IDisposable>();

            private bool _disposed;

            public RuntimeBindingValueProvider(IAttributeBinding binding)
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

                IWatchable watchable = provider as IWatchable;
                _watcher.Add(attribute.ToString(), watchable); // Add even if null to show name in status.

                IValueBinder binder = provider as IValueBinder;

                if (binder != null)
                {
                    _binders.Add(binder);
                }

                Debug.Assert(provider.Type == typeof(T));

                IDisposable disposable = provider as IDisposable;

                if (disposable != null)
                {
                    _disposables.Add(disposable);
                }

                return (T)provider.GetValue();
            }

            public void Dispose()
            {
                if (!_disposed)
                {
                    foreach (IDisposable disposable in _disposables)
                    {
                        disposable.Dispose();
                    }

                    _disposed = true;
                }
            }
        }

        private class CompositeSelfWatch : ISelfWatch
        {
            private ConcurrentDictionary<string, IWatchable> _watchables =
                new ConcurrentDictionary<string, IWatchable>();

            public void Add(string name, IWatchable watchable)
            {
                _watchables.AddOrUpdate(name, watchable, (ignore1, ignore2) => watchable);
            }

            public string GetStatus()
            {
                if (_watchables.Count == 0)
                {
                    return null;
                }

                // Show selfwatch from objects we've handed out. 
                StringBuilder builder = new StringBuilder();
                builder.AppendFormat("Created {0} object(s):", _watchables.Count);
                builder.AppendLine();

                foreach (KeyValuePair<string, IWatchable> watchable in _watchables)
                {
                    builder.Append(watchable.Key);

                    if (watchable.Value != null && watchable.Value.Watcher != null)
                    {
                        builder.Append(" ");
                        builder.Append(watchable.Value.Watcher.GetStatus());
                    }

                    builder.AppendLine();
                }

                return SelfWatch.EncodeSelfWatchStatus(builder.ToString());
            }
        }
    }
}
