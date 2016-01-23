using System.Collections.Concurrent;
using System.IO;
using Newtonsoft.Json;

namespace WebJobs.Script.WebHost
{
    public class SecretManager
    {
        private readonly string _secretsPath;
        private readonly ConcurrentDictionary<string, FunctionSecrets> _secretsMap = new ConcurrentDictionary<string, FunctionSecrets>();
        private readonly FileSystemWatcher _fileWatcher;

        public SecretManager(string secretsPath)
        {
            _secretsPath = secretsPath;

            Directory.CreateDirectory(_secretsPath);

            _fileWatcher = new FileSystemWatcher(_secretsPath, "*.json")
            {
                IncludeSubdirectories = true,
                EnableRaisingEvents = true
            };

            _fileWatcher.Changed += OnChanged;
            _fileWatcher.Created += OnChanged;
            _fileWatcher.Deleted += OnChanged;
            _fileWatcher.Renamed += OnChanged;
        }

        public FunctionSecrets GetFunctionSecrets(string functionName)
        {
            functionName = functionName.ToLowerInvariant();

            return _secretsMap.GetOrAdd(functionName, (n) =>
            {
                FunctionSecrets secrets;
                string secretFileName = string.Format("{0}.json", functionName);
                string secretFilePath = Path.Combine(_secretsPath, secretFileName);
                if (File.Exists(secretFilePath))
                {
                    // load the secrets file
                    string secretsJson = File.ReadAllText(secretFilePath);
                    secrets = JsonConvert.DeserializeObject<FunctionSecrets>(secretsJson);
                }
                else
                {
                    // initialize with empty instance
                    secrets = new FunctionSecrets();
                }

                return secrets;
            });
        }

        private void OnChanged(object sender, FileSystemEventArgs e)
        {
            // clear the cached secrets if they exist
            // they'll be reloaded on demand next time
            // they are needed
            string functionName = Path.GetFileNameWithoutExtension(e.FullPath).ToLowerInvariant();
            FunctionSecrets secrets;
            _secretsMap.TryRemove(functionName, out secrets);
        }
    }
}