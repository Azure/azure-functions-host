// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Reflection;

namespace Microsoft.Azure.WebJobs.Script
{
    public class MethodInvoker : IFunctionInvoker
    {
        private MethodInfo _method;

        public MethodInvoker(MethodInfo method)
        {
            _method = method;
        }

        public MethodInfo Target
        {
            get
            {
                return _method;
            }
        }

        public object Invoke(object[] parameters)
        {
            return _method.Invoke(null, parameters);
        }
    }
}
