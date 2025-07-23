public static string Run(Input input, out string blob1, out string blob2, TraceWriter log)
{
    blob1 = "Test Blob 1";
    blob2 = "Test Blob 2";

    return "Test Blob 3";
}

public class Input
{
    public string Id1 { get; set; }
    public string Id2 { get; set; }
    public string Id3 { get; set; }
}