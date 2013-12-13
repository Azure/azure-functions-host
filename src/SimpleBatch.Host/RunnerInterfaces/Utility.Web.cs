using System.IO;
using System.Net;
using System.Text;

namespace RunnerInterfaces
{
    public static partial class Utility
    {
        // Move to utility
        public static void PostJson(string url, object body)
        {
            var json = JsonCustom.SerializeObject(body);
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

        public static TResult PostJson<TResult>(string url, object body)
        {
            var result = Send<TResult>(url, "post", body);
            return result;
        }

        public static T GetJson<T>(string url)
        {
            WebRequest request = WebRequest.Create(url);
            request.Method = "GET";

            var response = request.GetResponse(); // does the actual web request

            var stream2 = response.GetResponseStream();
            var text = new StreamReader(stream2).ReadToEnd();
            stream2.Close();

            T val = JsonCustom.DeserializeObject<T>(text);
            return val;
        }
                
        public static T Send<T>(string uri, string verb, object body)
        {
            // Send 
            WebRequest request = WebRequest.Create(uri);
            request.Method = verb;

            if (body != null)
            {
                var json = JsonCustom.SerializeObject(body);
                byte[] bytes = Encoding.UTF8.GetBytes(json);

                request.ContentType = "application/json";
                request.ContentLength = bytes.Length; // set before writing to stream
                var stream = request.GetRequestStream();
                stream.Write(bytes, 0, bytes.Length);
                stream.Close();
            }

            var response = request.GetResponse(); // does the actual web request

            var stream2 = response.GetResponseStream();
            var text = new StreamReader(stream2).ReadToEnd();
            stream2.Close();

            T val = JsonCustom.DeserializeObject<T>(text);
            return val;
        }
    }
}