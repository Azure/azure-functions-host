using System.Reflection;
var assembly = typeof(Aspire.Hosting.DistributedApplication).Assembly;
var types = assembly.GetExportedTypes().Where(t => t.Name.Contains("Endpoint")).Select(t => t.FullName);
foreach (var t in types) Console.WriteLine(t);
