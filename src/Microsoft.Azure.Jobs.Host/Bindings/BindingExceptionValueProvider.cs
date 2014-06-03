using System;
using System.Globalization;

namespace Microsoft.Azure.Jobs.Host.Bindings
{
    internal class BindingExceptionValueProvider : IValueProvider
    {
        private readonly string _message;
        private readonly Exception _exception;

        public BindingExceptionValueProvider(string parameterName, Exception exception)
        {
            _message = exception.Message;
            _exception = new InvalidOperationException(String.Format(CultureInfo.InvariantCulture,
                "Exception binding parameter '{0}': {1}", parameterName, exception.Message));
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
