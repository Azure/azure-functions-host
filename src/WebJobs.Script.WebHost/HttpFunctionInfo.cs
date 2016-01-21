using Microsoft.Azure.WebJobs.Script;

namespace WebJobs.Script.WebHost
{
    public class HttpFunctionInfo
    {
        public FunctionDescriptor Function { get; set; }

        public string WebHookReceiver { get; set; }

        public string WebHookReceiverId { get; set; }

        public bool IsWebHook
        {
            get
            {
                return !string.IsNullOrEmpty(WebHookReceiver) &&
                       !string.IsNullOrEmpty(WebHookReceiverId);
            }
        }
    }
}