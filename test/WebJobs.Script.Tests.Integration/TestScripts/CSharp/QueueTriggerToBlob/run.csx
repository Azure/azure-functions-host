public static string Run(WorkItem input, ILogger log)
{
    string json = string.Format("{{ \"id\": \"{0}\" }}", input.Id);

    log.Info($"C# script processed queue message. Item={json}");

    return json;
}

public class WorkItem
{
    public string Id { get; set; }
}