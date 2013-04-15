using System;
using System.Collections.Generic;
using System.Data.Services.Client;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;
using Newtonsoft.Json;
using SimpleBatch;

namespace RunnerInterfaces
{
    public static partial class Utility
    {
        // Move to utility
        public static void PostJson(string url, object body)
        {
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(body);
            byte[] bytes = Encoding.UTF8.GetBytes(json);

            WebRequest request = WebRequest.Create(url);
            request.Method = "POST";
            request.ContentType = "application/json";
            request.ContentLength = bytes.Length; // set before writing to stream
            var stream = request.GetRequestStream();
            stream.Write(bytes, 0, bytes.Length);
            stream.Close();

            var response = request.GetResponse(); // does the actual web request
        }
                
        public static T Send<T>(string uri, string verb)
        {
            // Send 
            WebRequest request = WebRequest.Create(uri);
            request.Method = verb;
            request.ContentType = "application/json";
            request.ContentLength = 0;

            var response = request.GetResponse(); // does the actual web request

            var stream2 = response.GetResponseStream();
            var text = new StreamReader(stream2).ReadToEnd();
            stream2.Close();

            T val = JsonConvert.DeserializeObject<T>(text);
            return val;
        }
    }
}