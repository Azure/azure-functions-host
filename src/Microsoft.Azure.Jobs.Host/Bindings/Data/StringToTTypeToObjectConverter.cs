using System;
using Microsoft.Azure.Jobs.Host.Converters;

namespace Microsoft.Azure.Jobs.Host.Bindings.Data
{
    internal class StringToTTypeToObjectConverter<TInput> : ITypeToObjectConverter<TInput>
    {
        private Type _parameterType;

        public bool CanConvert(Type outputType)
        {
            if (typeof(TInput) != typeof(string))
            {
                return false;
            }

            if (!ObjectBinderHelpers.CanBindFromString(outputType))
            {
                return false;
            }

            // A slight abuse of the ITypeToObjectConverter contract, but it works for now.
            _parameterType = outputType;
            return true;
        }

        public object Convert(TInput input)
        {
            string value = input.ToString(); // Really (string)input, but the compiler can't verify that's possible.
            return ObjectBinderHelpers.BindFromString(value, _parameterType);
        }
    }
}
