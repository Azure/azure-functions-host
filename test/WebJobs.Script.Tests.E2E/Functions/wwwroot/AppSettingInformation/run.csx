using System.Configuration;

public static string Run(HttpRequestMessage req) => ConfigurationManager.AppSettings["FUNCTIONS_EXTENSION_VERSION"];