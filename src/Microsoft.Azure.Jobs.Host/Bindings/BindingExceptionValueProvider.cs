// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Globalization;

namespace Microsoft.Azure.WebJobs.Host.Bindings
{
    internal class BindingExceptionValueProvider : IValueProvider
    {
        private readonly string _message;
        private readonly Exception _exception;

        public BindingExceptionValueProvider(string parameterName, Exception exception)
        {
            _message = exception.Message;
            _exception = new InvalidOperationException(String.Format(CultureInfo.InvariantCulture,
                "Exception binding parameter '{0}'", parameterName), exception);
        }

        public Exception Exception
        {
            get { return _exception; }
        }

        public Type Type
        {
            get { return typeof(object); }
        }

        public object GetValue()
        {
            return null;
        }

        public string ToInvokeString()
        {
            return _message;
        }
    }
}
