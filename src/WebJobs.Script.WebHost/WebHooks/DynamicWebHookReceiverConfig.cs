using System.Threading.Tasks;
using Microsoft.AspNet.WebHooks;

namespace WebJobs.Script.WebHost.WebHooks
{
    public class DynamicWebHookReceiverConfig : IWebHookReceiverConfig
    {
        private readonly SecretsManager _secretsManager;

        public DynamicWebHookReceiverConfig(SecretsManager secretsManager)
        {
            _secretsManager = secretsManager;
        }

        public Task<string> GetReceiverConfigAsync(string name, string id)
        {
            // "id" will be the function name
            // we ignore the "name" parameter since we only allow a function
            // to be mapped to a single receiver
            FunctionSecrets secrets = _secretsManager.GetFunctionSecrets(id);
            if (secrets != null)
            {
                return Task.FromResult(secrets.WebHookReceiverKey);
            }

            return null;
        }
    }
}