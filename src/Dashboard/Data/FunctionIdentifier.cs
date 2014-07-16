// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace Dashboard.Data
{
    internal class FunctionIdentifier
    {
        private readonly string _hostId;
        private readonly string _hostFunctionId;

        public FunctionIdentifier(string hostId, string hostFunctionId)
        {
            _hostId = hostId;
            _hostFunctionId = hostFunctionId;
        }

        public string HostId
        {
            get { return _hostId; }
        }

        public string HostFunctionId
        {
            get { return _hostFunctionId; }
        }

        public static FunctionIdentifier Parse(string functionId)
        {
            int underscoreIndex = functionId.IndexOf('_');
            string hostId = functionId.Substring(0, underscoreIndex);
            string hostFunctionId = functionId.Substring(underscoreIndex + 1);
            return new FunctionIdentifier(hostId, hostFunctionId);
        }

        public override string ToString()
        {
            return _hostId + "_" + _hostFunctionId;
        }
    }
}
