// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.IO;
using System.Reflection;

namespace Microsoft.Azure.Jobs.Host.Bindings.ConsoleOutput
{
    internal class ConsoleOutputBindingProvider : IBindingProvider
    {
        public IBinding TryCreate(BindingProviderContext context)
        {
            ParameterInfo parameter = context.Parameter;

            if (parameter.ParameterType != typeof(TextWriter))
            {
                return null;
            }

            return new ConsoleOutputBinding(parameter.Name);
        }
    }
}
