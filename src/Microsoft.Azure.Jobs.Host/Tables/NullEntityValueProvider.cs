// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.Jobs.Host.Bindings;

namespace Microsoft.Azure.Jobs.Host.Tables
{
    internal class NullEntityValueProvider : IValueProvider
    {
        private readonly TableEntityContext _entityContext;
        private readonly Type _valueType;

        public NullEntityValueProvider(TableEntityContext entityContext, Type valueType)
        {
            _entityContext = entityContext;
            _valueType = valueType;
        }

        public Type Type
        {
            get { return _valueType; }
        }

        public object GetValue()
        {
            return null;
        }

        public string ToInvokeString()
        {
            return _entityContext.ToInvokeString();
        }
    }
}
