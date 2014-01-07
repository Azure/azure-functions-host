using SimpleBatch;
using System.ServiceModel.Syndication;
using System.IO;
using System.Xml;

public partial class TimerTestFuncs
{
    public static void Initialize(IConfiguration config)
    {
        config.Add<SyndicationFeed>(new SyndicationFeedBinder());
    }
}

// Custom model binder to bind between a SyndicationFeed and a stream.
class SyndicationFeedBinder : ICloudBlobStreamBinder<SyndicationFeed>
{
    public SyndicationFeed ReadFromStream(Stream input)
    {
        throw new System.NotImplementedException();
    }

    public void WriteToStream(SyndicationFeed result, Stream output)
    {
        using (var writer = XmlWriter.Create(output))
        {
            result.SaveAsRss20(writer);
        }
    }
}
