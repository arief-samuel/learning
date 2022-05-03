using Microsoft.SyndicationFeed;

namespace entities
{
    public enum FeedType
{
    Rss,
    Atom
}

public class FeedSourceStore
{
    public List<FeedSource> Sources { get;  set; } = new List<FeedSource>();
}

public class FeedItemStore
{
    public List<FeedItem> Results { get;  set; } = new List<FeedItem>();
}

public class FeedSource
{
    public string Url { get; set; } = string.Empty;
    public FeedType Type { get;  set; }
    public string Title { get;  set; } = string.Empty;
    public string? WebSite { get;  set; }
    public bool HideTitle { get;  set; }
    public bool HideDescription { get;  set; }
    public short UpdateFrequencyInMinutes { get; set; } = 1;

    public FeedChannel ToChannel()
    {
        return new FeedChannel {
            Title = Title,
            WebSite = WebSite,
            HideTitle = HideTitle,
            HideDescription = HideDescription
        };
    }
}

public record FeedChannel
{
    public string? Title { get;  set; }
    public string? WebSite { get;  set; }
    public Uri? Url { get;  set; }
    public bool HideDescription { get;  set; }
    public bool HideTitle { get;  set; }
}

public record FeedItem
{
    public FeedItem()
    {
        
    }
    public FeedItem(FeedChannel channel, SyndicationItem item)
    {
        Channel = channel;
        Id = item.Id;
        Title = item.Title;
        Description = item.Description;
        var link = item.Links.FirstOrDefault();
        if (link is object)
            Url = link.Uri;
        
        if (item.LastUpdated == default(DateTimeOffset))
            PublishedOn = item.Published;
        else 
            PublishedOn = item.LastUpdated;
        
    }

    public FeedChannel? Channel { get; private set; }
    public string? Id { get; internal set; }
    public string? Title { get; private set; }
    public string? Description { get; private set; }
    public DateTimeOffset PublishedOn { get; internal set; }
    public Uri? Url { get; private set; }
}
}