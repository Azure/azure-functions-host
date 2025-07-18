
public static TestPayload Run(TestPayload payload)
{
    return payload;
}

public class TestPayload
{
    public CustomType Custom { get; set; }

    public IEnumerable<CustomType> CustomEnumerable { get; set; }
}

public class CustomType
{
    public string CustomProperty { get; set; }
}