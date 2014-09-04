// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Azure.WebJobs.Host.Bindings;

namespace Microsoft.Azure.WebJobs.Host.Queues
{
    /// <summary>
    /// Implementation of <see cref="IBindableQueuePath"/> strategy for paths 
    /// containing one or more parameters.
    /// </summary>
    internal class ParameterizedQueuePath : IBindableQueuePath
    {
        private readonly string _queueNamePattern;
        private readonly IReadOnlyList<string> _parameterNames;

        public ParameterizedQueuePath(string queueNamePattern, IReadOnlyList<string> parameterNames)
        {
            Debug.Assert(parameterNames != null, "parameterNames must not be null");
            Debug.Assert(parameterNames.Count > 0, "parameterNames must contain one or more parameters");

            _queueNamePattern = queueNamePattern;
            _parameterNames = parameterNames;
        }

        public string QueueNamePattern
        {
            get { return _queueNamePattern; }
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
            string queueName = BindingDataPath.Resolve(QueueNamePattern, parameters);
            return BindableQueuePath.NormalizeAndValidate(queueName);
        }
    }
}
