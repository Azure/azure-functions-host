using System;
using System.IO;

namespace Microsoft.Azure.Jobs.Host.Bindings.ConsoleOutput
{
    internal sealed class ConsoleOutputValueProvider : IValueProvider
    {
        private readonly TextWriter _consoleOutput;

        public ConsoleOutputValueProvider(TextWriter consoleOutput)
        {
            _consoleOutput = consoleOutput;
        }

        public Type Type
        {
            get { return typeof(TextWriter); }
        }

        public object GetValue()
        {
            return _consoleOutput;
        }

        public string ToInvokeString()
        {
            return null;
        }
    }
}
