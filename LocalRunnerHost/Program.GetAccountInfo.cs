using System.Linq;
using System.Xml;
using System.Xml.Linq;
using System;
using RunnerInterfaces;

namespace LocalRunnerHost
{
    // Test harness for lcoally replyaing a failed job. 
    public partial class Program
    {
        // Get the storage account that the service is running against. 
        // This is that the Function Instance Guids get resolved against.         
        // Reads it from the same Azure configuration file  *.cscfg that the service uses.
        public static IAccountInfo GetAccountInfo(string configPath)
        {
            XDocument doc = XDocument.Load(configPath);

            var settings = from XElement x in doc.Descendants()
                           where x.Name.LocalName == "Setting"
                           select x;

            foreach (var setting in settings)
            {
                var attr = setting.Attribute("name");
                if (attr != null && attr.Value == "MainStorage")
                {
                    var attrValue = setting.Attribute("value");
                    if (attrValue != null)
                    {
                        string value = attrValue.Value;

                        return new AccountInfo
                        {
                             AccountConnectionString = value
                        };
                    }
                }
            }

            throw new InvalidOperationException("No account configuration in file: " + configPath);
        }
    }
}