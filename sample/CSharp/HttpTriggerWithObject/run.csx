public static TestObject Run(TestObject req)
{
    req.Greeting = $"Hello, {req.SenderName}";
    
    return req;
}

public class TestObject
{
    public string SenderName { get; set; }

    public string Greeting { get; set; }
}