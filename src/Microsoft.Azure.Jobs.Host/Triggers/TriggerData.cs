// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using Microsoft.Azure.Jobs.Host.Bindings;

namespace Microsoft.Azure.Jobs.Host.Triggers
{
    internal class TriggerData : ITriggerData
    {
        private readonly IValueProvider _valueProvider;
        private readonly IReadOnlyDictionary<string, object> _bindingData;

        public TriggerData(IValueProvider valueProvider, IReadOnlyDictionary<string, object> bindingData)
        {
            _valueProvider = valueProvider;
            _bindingData = bindingData;
        }

        public IValueProvider ValueProvider
        {
            get { return _valueProvider; }
        }

        public IReadOnlyDictionary<string, object> BindingData
        {
            get { return _bindingData; }
        }
    }
}
