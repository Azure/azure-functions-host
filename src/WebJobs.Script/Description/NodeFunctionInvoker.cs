// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using EdgeJs;
using Microsoft.Azure.WebJobs.Script.Binders;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script
{
    // TODO: make this internal
    public class NodeFunctionInvoker : IFunctionInvoker
    {
        private readonly Func<object, Task<object>> _scriptFunc;
        private static string FunctionTemplate;

        static NodeFunctionInvoker()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            using (StreamReader reader = new StreamReader(assembly.GetManifestResourceStream("Microsoft.Azure.WebJobs.Script.functionTemplate.js")))
            {
                FunctionTemplate = reader.ReadToEnd();
            }
        }

        public NodeFunctionInvoker(string scriptFilePath)
        {
            scriptFilePath = scriptFilePath.Replace('\\', '/');
            string script = string.Format(FunctionTemplate, scriptFilePath);
            _scriptFunc = Edge.Func(script);
        }

        public async Task Invoke(object[] parameters)
        {
            var context = CreateContext(parameters);

            await _scriptFunc(context);
        }

        private object CreateContext(object[] parameters)
        {
            object input = parameters[0];
            TextWriter textWriter = (TextWriter)parameters[1];
            IBinder binder = (IBinder)parameters[2];

            Type triggerParameterType = input.GetType();
            if (triggerParameterType == typeof(string))
            {
                // convert string into Dictionary which Edge will convert into an object
                // before invoking the function
                input = JsonConvert.DeserializeObject<Dictionary<string, object>>((string)input);
            }

            // create a TextWriter wrapper that can be exposed to Node.js
            var log = (Func<object, Task<object>>)((text) =>
            {
                textWriter.WriteLine(text);
                return Task.FromResult<object>(null);
            });

            var context = new
            {
                input = input,
                log = log,
                blob = BlobBinder.Create(binder)
            };

            return context;
        }
    }
}
