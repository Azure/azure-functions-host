// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using EdgeJs;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.Node
{
    public class ScriptInvoker : IFunctionInvoker
    {
        private readonly Func<object, Task<object>> _scriptFunc;

        public ScriptInvoker(string scriptFilePath)
        {
            scriptFilePath = scriptFilePath.Replace('\\', '/');
            string script = string.Format("return require('{0}');", scriptFilePath);

            _scriptFunc = Edge.Func(script);
        }

        public async Task Invoke(object[] parameters)
        {
            // TODO: Decide how to handle this
            Type triggerParameterType = parameters[0].GetType();
            if (triggerParameterType == typeof(string))
            {
                // convert string into Dictionary which Edge will convert into an object
                // before invoking the function
                parameters[0] = JsonConvert.DeserializeObject<Dictionary<string, object>>((string)parameters[0]);
            }

            // create a TextWriter wrapper that can be exposed to Node.js
            TextWriter textWriter = (TextWriter)parameters[1];
            var logFunc = (Func<object, Task<object>>)((text) =>
            {
                textWriter.WriteLine(text);
                return Task.FromResult<object>(null);
            });

            var context = new
            {
                input = parameters[0],
                log = logFunc
            };

            await _scriptFunc(context);
        }
    }
}
