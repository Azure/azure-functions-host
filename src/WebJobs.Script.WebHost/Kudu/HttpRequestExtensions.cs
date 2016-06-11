using System.Web;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Kudu
{
    public static class HttpRequestExtensions
    {
        public static bool IsFunctionsPortalRequest(this HttpRequest request)
        {
            return request.Headers[Constants.FunctionsPortal] != null;
        }
    }
}