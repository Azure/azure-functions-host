using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace SimpleBatch.Client
{
    public interface ISyncCall
    {
        Task InvokeAsync(string function, object args = null); // no return value
        Task<T> InvokeAsync<T>(string function, object args = null);
    }

    public class Client
    {
        const string defaultUri = @"http://daas2.azurewebsites.net/";

        string _serviceUri;

        public Client(string serviceUri = null)
        {
            _serviceUri = (serviceUri == null) ? defaultUri : serviceUri;            
        }

        // Task is signalled when function is done
        public Task SendAsync(string function, object parameters)
        {
            string url = MakeUri(function, parameters);

            WebRequest request = WebRequest.Create(url);
            request.Method = "GET";
            request.ContentType = "application/json";
            request.ContentLength = 0;

            // Common error: illegal function 
            var response = request.GetResponse(); // throws on errors and 404   

            
            // Poll waiting for 

            var tsc = new TaskCompletionSource<object>();
            tsc.SetResult(null);
            return tsc.Task;
        }

        private string MakeUri(string function, object parameters)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(_serviceUri);
            sb.Append("api/run/");
            sb.AppendFormat("?func={0}", function);

            foreach(var kv in ConvertObjectToDict(parameters))
            {
                sb.AppendFormat("&{0}={1}", kv.Key, kv.Value);
            }
            return sb.ToString();
        }

        // Include source copy of ObjectBinderHelpers. Don't want to take the assembly reference.
        IDictionary<string, string> ConvertObjectToDict(object parameters)
        {
            return RunnerInterfaces.ObjectBinderHelpers.ConvertObjectToDict(parameters);
        }
    }
}
