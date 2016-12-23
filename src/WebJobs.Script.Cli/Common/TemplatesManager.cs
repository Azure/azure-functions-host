using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Colors.Net;
using Newtonsoft.Json;
using WebJobs.Script.Cli.Interfaces;

namespace WebJobs.Script.Cli.Common
{
    internal class TemplatesManager : ITemplatesManager
    {
        public Task<IEnumerable<Template>> Templates
        {
            get
            {
                return GetTemplates();
            }
        }

        private static async Task<IEnumerable<Template>> GetTemplates()
        {
            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/");
                var templatesResponse = await client.GetAsync("https://functions.azure.com/api/templates?runtime=latest");
                return await templatesResponse.Content.ReadAsAsync<IEnumerable<Template>>();
            }
        }

        public async Task Deploy(string Name, Template template)
        {
            var path = Path.Combine(Environment.CurrentDirectory, Name);
            if (FileSystemHelpers.DirectoryExists(path))
            {
                var response = "n";
                do
                {
                    ColoredConsole.Write("A directory with the name {Name} already exists. Overwrite [y/n]? [n] ");
                    response = Console.ReadLine();
                } while (response != "n" && response != "y");
                if (response == "n")
                {
                    return;
                }
            }

            if (FileSystemHelpers.DirectoryExists(path))
            {
                FileSystemHelpers.DeleteDirectorySafe(path, ignoreErrors: false);
            }

            FileSystemHelpers.EnsureDirectory(path);

            foreach (var file in template.Files)
            {
                var filePath = Path.Combine(path, file.Key);
                ColoredConsole.WriteLine($"Writing {filePath}");
                await FileSystemHelpers.WriteAllTextToFileAsync(filePath, file.Value);
            }
            var functionJsonPath = Path.Combine(path, "function.json");
            ColoredConsole.WriteLine($"Writing {functionJsonPath}");
            await FileSystemHelpers.WriteAllTextToFileAsync(functionJsonPath, JsonConvert.SerializeObject(template.Function, Formatting.Indented));
        }
    }
}
