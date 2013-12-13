using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.ServiceModel.Syndication;
using System.Xml;
using SimpleBatch;

public partial class TimerTestFuncs
{
    // Aggregates to: http://<mystorage>.blob.core.windows.net/blog/output.rss.xml
    [Timer("00:10:00")] 
    public static void AggregateRss(
        [BlobOutput(@"blog\output.rss.xml")] out SyndicationFeed output
        )
    {
        string[] urls = new string[] { 
            @"http://rss.cnn.com/rss/cnn_latest.rss",
            @"http://blogs.msdn.com/b/mainfeed.aspx",
            @"http://blogs.msdn.com/b/jmstall/rss.aspx",
            @"http://news.yahoo.com/rss/"
        };

        List<SyndicationItem> items = new List<SyndicationItem>();
        foreach (string url in urls)
        {
            var reader = new XmlTextReader(url);
            var feed = SyndicationFeed.Load(reader);

            items.AddRange(feed.Items.Take(5));
        }
        var sorted = items.OrderBy(item => item.PublishDate);

        output = new SyndicationFeed("Status", "Status from SimpleBatch", null, sorted);        
    }
}

