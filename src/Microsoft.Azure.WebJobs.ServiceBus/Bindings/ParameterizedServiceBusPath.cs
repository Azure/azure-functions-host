// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Azure.WebJobs.Host.Bindings;

namespace Microsoft.Azure.WebJobs.ServiceBus.Bindings
{
    /// <summary>
    /// Omplementation of <see cref="IBindableServiceBusPath"/> strategy for paths 
    /// containing one or more parameters.
    /// </summary>
    internal class ParameterizedServiceBusPath : IBindableServiceBusPath
    {
        private readonly string _queueOrTopicNamePattern;
        private readonly IReadOnlyList<string> _parameterNames;

        public ParameterizedServiceBusPath(string queueOrTopicNamePattern, IReadOnlyList<string> parameterNames)
        {
            Debug.Assert(parameterNames != null, "parameterNames must not be null");
            Debug.Assert(parameterNames.Count > 0, "parameterNames must not be empty");

            _queueOrTopicNamePattern = queueOrTopicNamePattern;
            _parameterNames = parameterNames;
        }

        public string QueueOrTopicNamePattern
        {
            get { return _queueOrTopicNamePattern; }
        }

        public bool IsBound
        {
            get { return false; }
        }

        public IEnumerable<string> ParameterNames
        {
            get { return _parameterNames; }
        }

        public string Bind(IReadOnlyDictionary<string, object> bindingData)
        {
            if (bindingData == null)
            {
                throw new ArgumentNullException("bindingData");
            }
                
            IReadOnlyDictionary<string, string> parameters = BindingDataPath.GetParameters(bindingData);
            string queueOrTopicName = BindingDataPath.Resolve(QueueOrTopicNamePattern, parameters);
            return queueOrTopicName;
        }
    }
}
