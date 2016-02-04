// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    public abstract class ScriptFunctionInvokerBase : IFunctionInvoker
    {
        public abstract Task Invoke(object[] parameters);

        protected static Dictionary<string, string> GetBindingData(object value)
        {
            Dictionary<string, string> bindingData = new Dictionary<string, string>();

            try
            {
                // parse the object skipping any nested objects (binding data
                // only includes top level properties)
                JObject parsed = JObject.Parse(value as string);
                bindingData = parsed.Children<JProperty>()
                    .Where(p => p.Value.Type != JTokenType.Object)
                    .ToDictionary(p => p.Name, p => (string)p);
            }
            catch
            {
                // it's not an error if the incoming message isn't JSON
                // there are cases where there will be output binding parameters
                // that don't bind to JSON properties
            }

            return bindingData;
        }
    }
}
