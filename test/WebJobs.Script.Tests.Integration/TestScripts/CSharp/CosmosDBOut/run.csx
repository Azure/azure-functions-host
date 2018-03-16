public static object Run(string input, ICollector<dynamic> relatedItems)
{
    // a later stage of the test will query for these
    for (int i = 0; i < 3; i++)
    {
        relatedItems.Add(new { related = input });
    }

    return new
    {
        id = input,
        text = "Hello from C#!"
    };
}