using System;
using System.Collections.Generic;
using System.Configuration;

namespace WebJobs.Client
{
    class Program
    {
        static void Main(string[] args)
        {
            WebJobClient client = CreateClient();
            if (client == null)
            {
                Console.ReadKey();
            }

            do
            {
                Console.Write(">> ");

                string functionCommand = Console.ReadLine();
                string functionName;
                Dictionary<string, string> arguments;
                if (ParseCommand(functionCommand, out functionName, out arguments))
                {
                    Console.WriteLine();
                    Guid functionInvocationId = client.InvokeFunction(functionName, arguments);
                    client.WriteInvocationResults(functionName, functionInvocationId);
                    Console.WriteLine();
                }             
            }
            while (true);
        }

        private static WebJobClient CreateClient()
        {
            string hostId = GetSettingFromConfigOrEnvironment("AzureWebJobsHostId");
            if (string.IsNullOrEmpty(hostId))
            {
                WebJobClient.WriteWithColor("Host Id must be set via an app setting or environment variable (AzureWebJobsHostId).", ConsoleColor.Yellow);
                return null;
            }
            string storageConnectionString = GetConnectionFromConfigOrEnvironment("AzureWebJobsStorage");
            if (string.IsNullOrEmpty(storageConnectionString))
            {
                WebJobClient.WriteWithColor("Storage connection string must be set via an app setting or environment variable (AzureWebJobsStorage).", ConsoleColor.Yellow);
                return null;
            }

            string functionTimeoutSetting = GetSettingFromConfigOrEnvironment("AzureWebJobsFunctionTimeout");
            TimeSpan functionTimeout = !string.IsNullOrEmpty(functionTimeoutSetting) ? TimeSpan.Parse(functionTimeoutSetting) : TimeSpan.FromSeconds(30);

            return new WebJobClient(hostId, storageConnectionString, functionTimeout);
        }

        private static bool ParseCommand(string command, out string name, out Dictionary<string, string> arguments)
        {
            name = null;
            arguments = new Dictionary<string, string>();

            try
            {
                if (command == null || command.Length == 0)
                {
                    return false;
                }

                int idx = command.IndexOf(' ');
                if (idx > 0)
                {
                    name = command.Substring(0, idx);
                }
                else
                {
                    // no parameters
                    name = command;
                    arguments["input"] = null;
                    return true;
                }

                string input = command.Substring(idx + 1);
                idx = input.IndexOf(':');
                if (idx == 0)
                {
                    // default parameter name and value
                    arguments["input"] = null;
                }
           
                string paramName = input.Substring(0, idx).Trim();
                string paramValue = input.Substring(idx + 1).Trim();
                arguments[paramName] = paramValue;

                return true;
            }
            catch
            {
                Console.WriteLine("Invalid command");
                return false;
            }
        }

        public static string GetConnectionFromConfigOrEnvironment(string connectionName)
        {
            string configValue = null;
            var connectionStringEntry = ConfigurationManager.ConnectionStrings[connectionName];
            if (connectionStringEntry != null)
            {
                configValue = connectionStringEntry.ConnectionString;
            }

            if (!string.IsNullOrEmpty(configValue))
            {
                // config values take precedence over environment values
                return configValue;
            }

            return Environment.GetEnvironmentVariable(connectionName) ?? configValue;
        }

        public static string GetSettingFromConfigOrEnvironment(string settingName)
        {
            string configValue = ConfigurationManager.AppSettings[settingName];
            if (!string.IsNullOrEmpty(configValue))
            {
                // config values take precedence over environment values
                return configValue;
            }

            return Environment.GetEnvironmentVariable(settingName) ?? configValue;
        }
    }
}
