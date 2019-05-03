// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace ExtensionsMetadataGenerator
{
    /// <summary>
    ///  A simple logger.
    /// </summary>
    public class ConsoleLogger
    {
        private const int SpacesPerIndent = 2;
        private int _indent = 0;

        public IDisposable Indent()
        {
            return new IndentDisposable(this);
        }

        private void PushIndent()
        {
            _indent++;
        }

        private void PopIndent()
        {
            if (--_indent < 0)
            {
                _indent = 0;
            }
        }

        public void LogMessage(string message)
        {
            System.Console.WriteLine(Indent(message));
        }

        public void LogError(string message)
        {
            System.Console.Error.WriteLine(Indent(message));
        }

        private string Indent(string message)
        {
            return message.PadLeft(message.Length + (_indent * SpacesPerIndent));
        }

        private class IndentDisposable : IDisposable
        {
            private readonly ConsoleLogger _logger;

            public IndentDisposable(ConsoleLogger logger)
            {
                _logger = logger;
                _logger.PushIndent();
            }

            public void Dispose()
            {
                _logger.PopIndent();
            }
        }
    }
}
