using System.Net;
using System.Xml;
using Microsoft.SyndicationFeed;
using Microsoft.SyndicationFeed.Atom;
using Microsoft.SyndicationFeed.Rss;
using Orleans;
using Orleans.Configuration;
using Orleans.Hosting;
using Orleans.Runtime;

var builder = WebApplication.CreateBuilder();
builder.Services.AddHttpClient();
builder.Logging
    .SetMinimumLevel(LogLevel.Information)
    .AddConsole();
builder.Host.UseOrleans(orleans => {
    orleans
        .UseLocalhostClustering()
        .UseInMemoryReminderService()
        .Configure<ClusterOptions>(opt => {
            opt.ClusterId = "dev";
            opt.ServiceId = "http-client";
        })
        .Configure<EndpointOptions>(opt => opt.AdvertisedIPAddress = IPAddress.Loopback)
        .ConfigureApplicationParts(parts => parts.AddApplicationPart(typeof(FeedSourceGrain).Assembly).WithReferences())
        .AddRedisGrainStorage("redis-rss-reader", optBuilder => optBuilder.Configure(opt => {
            opt.ConnectionString = "localhost:6379";
            opt.UseJson = true;
            opt.DatabaseNumber = 1;
        }));
});
var app = builder.Build();

/*
app.MapGet("/", async context =>
{
    var client = context.RequestServices.GetService<IGrainFactory>()!;
    var feedSourceGrain = client.GetGrain<IFeedSource>(0)!;

    await feedSourceGrain.Add(new FeedSource
    {
        Type = FeedType.Rss,
        Url = "http://www.scripting.com/rss.xml",
        Website = "http://www.scripting.com",
        Title = "Scripting News"
    });

    await feedSourceGrain.Add(new FeedSource
    {
        Type = FeedType.Atom,
        Url = "https://www.reddit.com/r/dotnet.rss",
        Website = "https://www.reddit.com/r/dotnet",
        Title = "Reddit/r/dotnet"
    });

    var sources = await feedSourceGrain.GetAll();

    foreach (var s in sources)
    {
        var feedFetcherGrain = client.GetGrain<IFeedFetcher>(s.Url.ToString());
        await feedFetcherGrain.Fetch(s);
    }

    var feedResultsGrain = client.GetGrain<IFeedItemResults>(0);
    var feedItems = await feedResultsGrain.GetAll();

    await context.Response.WriteAsync(@"<html>
                    <head>
                        <link rel=""stylesheet"" href=""https://cdn.jsdelivr.net/npm/uikit@3.5.5/dist/css/uikit.min.css"" />
                        <title>Orleans RSS Reader</title>
                    </head>");
    await context.Response.WriteAsync("<body><div class=\"uk-container\">");
    await context.Response.WriteAsync("<ul class=\"uk-list\">");
    foreach (var i in feedItems)
    {
        await context.Response.WriteAsync("<li class=\"uk-card uk-card-default uk-card-body\">");
        if (!string.IsNullOrWhiteSpace(i.Title))
            await context.Response.WriteAsync($"{ i.Title }<br/>");

        await context.Response.WriteAsync(i.Description ?? "");

        if (i.Url is object)
            await context.Response.WriteAsync($"<br/><a href=\"{i.Url}\">link</a>");

        await context.Response.WriteAsync($"<div style=\"font-size:small;\">published on: {i.PublishedOn}</div>");
        await context.Response.WriteAsync($"<div style=\"font-size:small;\">source: <a href=\"{i.Channel?.Website}\">{i.Channel?.Title}</a></div>");
        await context.Response.WriteAsync("</li>");
    }
    await context.Response.WriteAsync("</ul>");
    await context.Response.WriteAsync("</div></body></html>");
});
*/

app.MapGet("/", async ctx => {
    var client = ctx.RequestServices.GetService<IGrainFactory>()!;
    var feedSourceGrain = client.GetGrain<IFeedSource>(0)!;

    await feedSourceGrain.Add(new FeedSource
    {
        Type = FeedType.Rss,
        Url = "http://www.scripting.com/rss.xml",
        Website = "http://www.scripting.com",
        Title = "Scripting News"
    });

    await feedSourceGrain.Add(new FeedSource
    {
        Type = FeedType.Atom,
        Url = "https://www.reddit.com/r/dotnet.rss",
        Website = "https://www.reddit.com/r/dotnet",
        Title = "Reddit/r/dotnet"
    });

    var sources = await feedSourceGrain.GetAll();
    foreach (var s in sources)
    {
        var feedFetcherGrain = client.GetGrain<IFeedFetcher>(s.Url.ToString());
        await feedFetcherGrain.Fetch(s);
    }

    var feedResultsGrain = client.GetGrain<IFeedItemResults>(0);
    var feedItems = await feedResultsGrain.GetAll();

    await ctx.Response.WriteAsync(@"
        <html>
            <head>
                <link rel=""stylesheet"" href=""https://cdn.jsdelivr.net/npm/uikit@3.5.5/dist/css/uikit.min.css"" />
                <title>Orleans RSS Reader</title>
            </head>");
    await ctx.Response.WriteAsync(@"<body>
        <div class=""uk-container"">");
    await ctx.Response.WriteAsync(@"<ul class=""uk-list"">");
    
    foreach (var i in feedItems)
    {
        await ctx.Response.WriteAsync(@"<li class=""uk-card uk-card-default uk-card-body"">");
        if (!string.IsNullOrWhiteSpace(i.Title))
            await ctx.Response.WriteAsync($"{i.Title}<br/>");
        
        await ctx.Response.WriteAsync(i.Description ?? "");

        if (i.Url is object)
            await ctx.Response.WriteAsync(@$"<br/><a href=""{i.Url}"">link</a>");
        
        await ctx.Response.WriteAsync($"<div style=\"font-size:small;\">published on: {i.PublishedOn}</div>");
        await ctx.Response.WriteAsync($"<div style=\"font-size:small;\">source: <a href=\"{i.Channel?.Website}\">{i.Channel?.Title}</a></div>");
        await ctx.Response.WriteAsync("</li>");
    } 
    await ctx.Response.WriteAsync("</ul>");
    await ctx.Response.WriteAsync("</div></body></html>");
});
app.Run();
public interface IFeedFetcher : IGrainWithStringKey
{
    Task Fetch(FeedSource source);
}
public interface IFeedSource : IGrainWithIntegerKey
{
    Task Add(FeedSource source);
    Task<List<FeedSource>> GetAll();
}
public interface IFeedItemResults : IGrainWithIntegerKey
{
    Task Add(List<FeedItem> items);
    Task<List<FeedItem>> GetAll();
    Task Clear();
}
public class FeedFetchGrain : Grain, IFeedFetcher
{
    private readonly IGrainFactory _factory;

    public FeedFetchGrain(
        IGrainFactory factory
    )
    {
        _factory = factory;
    }
    public async Task Fetch(FeedSource source)
    {
        var storage = _factory.GetGrain<IFeedItemResults>(0);
        var result = await ReadFeed(source);
        await storage.Add(result);
    }

    public async Task<List<FeedItem>> ReadFeed(FeedSource source)
    {
        var feed = new List<FeedItem>();
        try
        {
            using var xmlReader = XmlReader.Create(
                source.Url.ToString(),
                new XmlReaderSettings() {
                    Async = true
                });
            if (source.Type == FeedType.Rss)
            {
                var feedReader = new RssFeedReader(xmlReader);

                // Read the feed
                while (await feedReader.Read())
                {
                    switch (feedReader.ElementType)
                    {
                        // read item
                        case SyndicationElementType.Item:
                            var item = await feedReader.ReadItem();
                            feed.Add(new FeedItem(
                                source.ToChannel(),
                                new SyndicationItem(item)));
                            break;
                        default:
                            var content = await feedReader.ReadContent();
                            break;

                    }
                }
            }
            else
            {
                var feedReader = new AtomFeedReader(xmlReader);

                // Read the feed
                while (await feedReader.Read())
                {
                    switch (feedReader.ElementType)
                    {
                        // read item
                        case SyndicationElementType.Item:
                            var entry = await feedReader.ReadEntry();
                            feed.Add(new FeedItem(
                                source.ToChannel(),
                                new SyndicationItem(entry)));
                            break;
                        default:
                            var content = await feedReader.ReadContent();
                            break;

                    }
                }
            }
            return feed;
        }
        catch
        {
            return new List<FeedItem>();
        }
    }
}
public class FeedSourceGrain : Grain, IFeedSource
{
    private readonly IPersistentState<FeedSourceStore> _storage;

    public FeedSourceGrain(
        [PersistentState("feed-source", "redis-rss-reader")]
        IPersistentState<FeedSourceStore> storage
    )
    {
        _storage = storage;
    }
    public async Task Add(FeedSource source)
    {
        if (_storage.State.Sources.Find(x => x.Url == source.Url) is null)
            _storage.State.Sources.Add(source);
            await _storage.WriteStateAsync();
    }

    public Task<List<FeedSource>> GetAll() => Task.FromResult(_storage.State.Sources);
}
public class FeedItemResultGrain : Grain, IFeedItemResults
{
    private readonly IPersistentState<FeedItemStore> _storage;

    public FeedItemResultGrain(
        [PersistentState("feed-item-results", "redis-rss-reader")]
        IPersistentState<FeedItemStore> storage
    )
    {
        _storage = storage;
    }
    public async Task Add(List<FeedItem> items)
    {
        foreach (var item in items.Where(x => !string.IsNullOrWhiteSpace(x.Id)))
        {
            if (!_storage.State.Results.Exists(x => x.Id?.Equals(item.Id, StringComparison.OrdinalIgnoreCase) ?? false))
                _storage.State.Results.Add(item);         
        }
        await _storage.WriteStateAsync();
    }

    public async Task Clear()
    {
        _storage.State.Results.Clear();
        await _storage.WriteStateAsync();
    }

    public Task<List<FeedItem>> GetAll() => Task.FromResult(_storage.State.Results.OrderByDescending(x => x.PublishedOn).ToList());
}
public class FeedItemStore
{
    public List<FeedItem> Results { get; set; } = new List<FeedItem>();
}
public class FeedSourceStore
{
    public List<FeedSource> Sources { get;  set; } = new List<FeedSource>();
}
public enum FeedType
{
    Rss,
    Atom
}
public class FeedItem
{
    public FeedChannel? Channel { get; set; }
    public string? Id { get; set; }
    public string? Title  { get; set; }
    public string? Description { get; set; }
    public Uri? Url { get; set; }
    public int MyProperty { get; set; }
    public DateTimeOffset PublishedOn { get;  set; }
    
    public FeedItem()
    {
    }
    public FeedItem(
        FeedChannel channel,
        SyndicationItem item
    )
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

}
public class FeedSource
{
    public string Url { get;  set; } = string.Empty;
    public string Title { get;  set; } = string.Empty;
    public string? Website { get;  set; }
    public FeedType Type { get;  set; }

    public bool HideTitle { get; set; }
    public bool HideDescription { get; set; }

    internal FeedChannel ToChannel()
    {
        return new FeedChannel
        {
            Title = Title,
            Website = Website,
            HideTitle = HideTitle,
            HideDescription = HideDescription
        };
    }
}
public class FeedChannel
{
    public string Title { get;  set; }
    public string Website { get;  set; }
    public bool HideTitle { get;  set; }
    public bool HideDescription { get;  set; }
}