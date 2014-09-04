// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Azure.WebJobs.Host.Bindings;

namespace Microsoft.Azure.WebJobs.ServiceBus.Bindings
{
    /// <summary>
    /// Utility class with factory method to create an instance of a strategy class implementing <see cref="IBindableServiceBusPath"/> interface.
    /// </summary>
    internal static class BindableServiceBusPath
    {
        /// <summary>
        /// A factory method detecting parameters in supplied queue or topic name pattern and creating 
        /// an instance of relevant strategy class implementing <see cref="IBindableServiceBusPath"/>.
        /// </summary>
        /// <param name="queueOrTopicNamePattern">Service Bus queue or topic name pattern containing optional binding parameters.</param>
        /// <returns>An object implementing <see cref="IBindableServiceBusPath"/></returns>
        public static IBindableServiceBusPath Create(string queueOrTopicNamePattern)
        {
            if (queueOrTopicNamePattern == null)
            {
                throw new ArgumentNullException("queueOrTopicNamePattern");
            }

            List<string> parameterNames = new List<string>();
            BindingDataPath.AddParameterNames(queueOrTopicNamePattern, parameterNames);

            if (parameterNames.Count > 0)
            {
                return new ParameterizedServiceBusPath(queueOrTopicNamePattern, parameterNames);
            }

            return new BoundServiceBusPath(queueOrTopicNamePattern);
        }
    }
}
