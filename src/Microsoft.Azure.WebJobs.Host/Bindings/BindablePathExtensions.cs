// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;

namespace Microsoft.Azure.WebJobs.Host.Bindings
{
    internal static class BindablePathExtensions
    {
        public static void ValidateContractCompatibility<TPath>(this IBindablePath<TPath> path,
            IReadOnlyDictionary<string, Type> bindingDataContract)
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
                        throw new InvalidOperationException("No binding parameter exists for '" + parameterName + "'.");
                    }
                }
            }
        }
    }
}
