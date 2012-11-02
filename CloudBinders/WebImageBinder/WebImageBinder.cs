using SimpleBatch;
using System.Web.Helpers; // Install nuget package: system.web.helper
using System.IO;

public class WebImageBinder : ICloudBlobStreamBinder<WebImage>
{
    // Discovered by reflection 
    // a lot like ASP.Net's Global.asax...
    public static void Initialize(IConfiguration config)
    {
        config.Add<WebImage>(new WebImageBinder());
    }

    public WebImage ReadFromStream(Stream input)
    {
        return new WebImage(input);
    }

    public void WriteToStream(WebImage result, Stream output)
    {
        var bytes = result.GetBytes();
        output.Write(bytes, 0, bytes.Length);
    }
}
