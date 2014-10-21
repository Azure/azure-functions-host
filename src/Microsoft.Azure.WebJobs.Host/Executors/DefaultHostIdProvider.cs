// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Indexers;

namespace Microsoft.Azure.WebJobs.Host.Executors
{
    internal class DefaultHostIdProvider : IHostIdProvider
    {
        private readonly Func<IFunctionIndexProvider> _getFunctionIndexProvider;
        private readonly IStorageAccountProvider _storageAccountProvider;

        private string _hostId;
        private IHostIdProvider _innerProvider;

        public DefaultHostIdProvider(Func<IFunctionIndexProvider> getFunctionIndexProvider,
            IStorageAccountProvider storageAccountProvider)
        {
            _getFunctionIndexProvider = getFunctionIndexProvider;
            _storageAccountProvider = storageAccountProvider;
        }

        public string HostId
        {
            get
            {
                return _hostId;
            }
            set
            {
                if (value != null && !HostIdValidator.IsValid(value))
                {
                    throw new ArgumentException(HostIdValidator.ValidationMessage, "value");
                }

                _hostId = value;
                _innerProvider = null;
            }
        }

        public IHostIdProvider InnerProvider
        {
            get
            {
                if (_innerProvider == null)
                {
                    if (_hostId != null)
                    {
                        _innerProvider = new FixedHostIdProvider(_hostId);
                    }
                    else
                    {
                        _innerProvider = new DynamicHostIdProvider(_storageAccountProvider,
                            _getFunctionIndexProvider.Invoke());
                    }
                }

                return _innerProvider;
            }
        }

        public Task<string> GetHostIdAsync(CancellationToken cancellationToken)
        {
            return InnerProvider.GetHostIdAsync(cancellationToken);
        }
    }
}
