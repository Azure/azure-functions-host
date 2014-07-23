// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
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

        public async Task SetValueAsync(object value, CancellationToken cancellationToken)
        {
            foreach (IValueBinder binder in _binders)
            {
                // RuntimeBinding can only be uses for non-Out parameters, and their binders ignore this argument.
                await binder.SetValueAsync(value: null, cancellationToken: cancellationToken);
            }
        }

        public string ToInvokeString()
        {
            return null;
        }

        public async Task<TValue> BindAsync<TValue>(Attribute attribute, CancellationToken cancellationToken)
        {
            IBinding binding = await _bindingSource.BindAsync<TValue>(attribute, cancellationToken);

            if (binding == null)
            {
                throw new InvalidOperationException("No binding found for attribute '" + attribute.GetType() + "'.");
            }

            IValueProvider provider = await binding.BindAsync(_bindingSource.BindingContext);

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
