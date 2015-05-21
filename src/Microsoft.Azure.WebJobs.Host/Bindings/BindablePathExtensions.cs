// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;

namespace Microsoft.Azure.WebJobs.Host.Bindings
{
    internal static class BindablePathExtensions
    {
        public static void ValidateContractCompatibility<TPath>(this IBindablePath<TPath> path, IReadOnlyDictionary<string, Type> bindingDataContract)
        {
            if (path == null)
            {
                throw new ArgumentNullException("path");
            }

            IEnumerable<string> parameterNames = path.ParameterNames;

            if (parameterNames != null)
            {
                foreach (string parameterName in parameterNames)
                {
                    if (bindingDataContract != null && !bindingDataContract.ContainsKey(parameterName))
                    {
                        throw new InvalidOperationException(string.Format(CultureInfo.CurrentCulture, "No binding parameter exists for '{0}'.", parameterName));
                    }
                }
            }
        }
    }
}
