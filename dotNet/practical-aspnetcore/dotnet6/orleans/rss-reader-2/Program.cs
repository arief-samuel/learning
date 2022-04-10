using System.Net;
using Orleans;
using Orleans.Runtime;
using Orleans.Configuration;
using Orleans.Hosting;
using System.Xml;
using Microsoft.SyndicationFeed.Atom;
using Microsoft.SyndicationFeed;
using Microsoft.SyndicationFeed.Rss;

var builder = WebApplication.CreateBuilder();
builder.Services.AddHttpClient();
builder.Logging.SetMinimumLevel(LogLevel.Information).AddConsole();
builder.Host.UseOrleans(builder => {
    builder
        .UseLocalhostClustering()
        .UseInMemoryReminderService()
        .Configure<ClusterOptions>(opt => {
            opt.ClusterId = "dev";
            opt.ServiceId = "http-client";
        })
        .Configure<EndpointOptions>(opt => opt.AdvertisedIPAddress = IPAddress.Loopback)
        .ConfigureApplicationParts(parts => parts.AddApplicationPart(typeof(FeedSourceGrain).Assembly).WithReferences())
        .AddRedisGrainStorage("redis-rss-reader-2", optBuilder => optBuilder.Configure(opt => {
            opt.ConnectionString = "192.168.24.43:6379";
            opt.UseJson = true;
            opt.DatabaseNumber = 1;
        }));
});

var app = builder.Build();
app.MapGet("/", async ctx => {
    var client  = ctx.RequestServices.GetService<IGrainFactory>()!;
    var feedSourceGrain = client.GetGrain<IFeedSource>(0)!;

    await feedSourceGrain.AddAsync(new FeedSource{
        Type = FeedType.Rss,
        Url = "http://www.scripting.com/rss.xml",
        WebSite = "http://www.scripting.com",
        Title = "Scripting News",
        UpdateFrequencyInMinutes = 15
    });

    await feedSourceGrain.AddAsync( new FeedSource {
        Type = FeedType.Atom,
        Url = "https://www.reddit.com/r/dotnet.rss",
        WebSite = "https://www.reddit.com/r/dotnet",
        Title = "Reddit/r/dotnet",
        UpdateFrequencyInMinutes = 1
    });

    var sources = await feedSourceGrain.GetAllAsync();

    foreach (var s in sources)
    {
        var feedFetcherReminderGrain = client.GetGrain<IFeedFetcherReminder>(0)!;
        //add reminder is indempotent
        await feedFetcherReminderGrain.AddReminder(s.Url, s.UpdateFrequencyInMinutes);
    }

    var feedResultsGrain = client.GetGrain<IFeedItemResults>(0)!;
    var feedItems = await feedResultsGrain.GetAllAsync();

    await ctx.Response.WriteAsync(@"
        <html>
    <head>
        <link rel=""stylesheet"" href=""https://cdn.jsdelivr.net/npm/uikit@3.5.5/dist/css/uikit.min.css"" />
        <title>Orleans RSS Reader</title>
    </head>
    ");
    await ctx.Response.WriteAsync(@"
    <body>
        <div class=""uk-container"">");
    if (feedItems.Count == 0)
        await ctx.Response.WriteAsync("<p>Please refresh your browser again if you see no feed displayed.</p>");
        
    await ctx.Response.WriteAsync(@"
        <ul class=""uk-list"">");
    foreach (var i in feedItems)
    {
        await ctx.Response.WriteAsync(@"
        <li class=""uk-card uk-card-default uk-card-body"">");

        if (!string.IsNullOrWhiteSpace(i.Title))
            await ctx.Response.WriteAsync($"{i.Title} <br/>");
        
        await ctx.Response.WriteAsync(i.Description ?? "");

        if (i.Url is object)
            await ctx.Response.WriteAsync($@"<br/> <a href=""{i.Url}"">link</a>");
    
    await ctx.Response.WriteAsync($@"
        <div style=""font-size: small;"">Published on: {i.PublishedOn}</div>");
    await ctx.Response.WriteAsync($@"
        <div style=""font-size: small;"">Source on: <a    href=""{i.Channel?.WebSite}"">{i.Channel?.Title}</a></div>");
    await ctx.Response.WriteAsync("</li>");
        
    }
    await ctx.Response.WriteAsync("</ul>");
    await ctx.Response.WriteAsync(@"
        </div>
    </body>
</html>");
});

app.Run();

public interface IFeedItemResults : IGrainWithIntegerKey
{
    Task AddAsync(List<FeedItem> items);
    Task<List<FeedItem>> GetAllAsync();
    Task ClearAsync();
}

public interface IFeedFetcherReminder : IGrainWithIntegerKey
{
    Task AddReminder(string reminder, short repeatEveryMinute);
}

public interface IFeedSource : IGrainWithIntegerKey
{
    Task AddAsync(FeedSource source);
    Task<List<FeedSource>> GetAllAsync();
    Task<FeedSource?> FindFeedSourceByUrlAsync(string url);
}

public interface IFeedFetcher :IGrainWithStringKey
{
    Task FetchAsync(FeedSource source);
}
public class FeedItemResultGrain : Grain, IFeedItemResults
{
    private IPersistentState<FeedItemStore> _storage;

    public FeedItemResultGrain(
        [PersistentState("feed-item-results-2", "redis-rss-reader-2")]
        IPersistentState<FeedItemStore> storage
    )
    {
        _storage = storage;
    }
      public async Task AddAsync(List<FeedItem> items)
    {
        //make sure there is no duplication
        foreach (var i in items.Where(x => !string.IsNullOrWhiteSpace(x.Id)))
        {
            if (!_storage.State.Results.Exists(x => x.Id?.Equals(i.Id, StringComparison.OrdinalIgnoreCase) ?? false))
                _storage.State.Results.Add(i);
        }
        await _storage.WriteStateAsync();
    }


    public async Task ClearAsync()
    {
        _storage.State.Results.Clear();
        await _storage.WriteStateAsync();
    }

    public Task<List<FeedItem>> GetAllAsync()
    {
        return Task.FromResult(_storage.State.Results.OrderByDescending(x => x.PublishedOn).ToList());
    }
}

public class FeedSourceGrain : Grain, IFeedSource
{
    private readonly IPersistentState<FeedSourceStore> _storage;

    public FeedSourceGrain(
        [PersistentState("feed-source-2", "redis-rss-reader-2")]
        IPersistentState<FeedSourceStore> storage
    )
    {
        _storage = storage;
    }

    public async Task AddAsync(FeedSource source)
    {
        if (_storage.State.Sources.Find(x => x.Url == source.Url) is null)
        {
            _storage.State.Sources.Add(source);
            await _storage.WriteStateAsync();
        }
    }

    public Task<FeedSource?> FindFeedSourceByUrlAsync(string url)
    {
        return Task.FromResult(_storage.State.Sources.Find(x => x.Url.Equals(url, StringComparison.OrdinalIgnoreCase)));
    }

    public Task<List<FeedSource>> GetAllAsync()
    {
        return Task.FromResult(_storage.State.Sources);
    }
}

public class FeedFetchGrain : Grain, IFeedFetcher
{
    private readonly IGrainFactory _grainFactory;

    public FeedFetchGrain(
        IGrainFactory grainFactory
    )
    {
        _grainFactory = grainFactory;
    }
    public async Task FetchAsync(FeedSource source)
    {
        var storage = _grainFactory.GetGrain<IFeedItemResults>(0);
        var results = await ReadFeedAsync(source);
        await storage.AddAsync(results);
    }

    public async Task<List<FeedItem>> ReadFeedAsync(FeedSource source)
    {
        var feed = new List<FeedItem>();
        try
        {
            using var xmlReader = XmlReader.Create(source.Url.ToString(), new XmlReaderSettings() {
                Async = true
            });

            if (source.Type == FeedType.Rss)
            {
                var feedReader = new RssFeedReader(xmlReader);

                //read the feed
                while (await feedReader.Read())
                {
                    switch (feedReader.ElementType)
                    {
                        //Read Item
                        case SyndicationElementType.Item:
                            var item = await feedReader.ReadItem();
                            feed.Add(new FeedItem(source.ToChannel(), new SyndicationItem(item)));
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

                while (await feedReader.Read())
                {
                    switch (feedReader.ElementType)
                    {
                        
                        //Read Item
                        case SyndicationElementType.Item:
                            var entry = await feedReader.ReadEntry();
                            feed.Add(new FeedItem(source.ToChannel(), new SyndicationItem(entry)));
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

public class FeedFetcherReminder : Grain, IRemindable ,IFeedFetcherReminder
{
    private readonly IGrainFactory _grainFactory;
    private readonly ILogger _logger;

    public FeedFetcherReminder(
        IGrainFactory grainFactory,
        ILogger<FeedFetcherReminder> logger
    )
    {
        _grainFactory = grainFactory;
        _logger = logger;
    }
    public async Task AddReminder(string reminder, 
                            short repeatEveryMinute)
    {
        if (string.IsNullOrWhiteSpace(reminder))
            throw new ArgumentNullException(nameof(reminder));
        
        var r = await GetReminder(reminder);

        if (r is not object)
            await RegisterOrUpdateReminder(reminder,dueTime: TimeSpan.FromSeconds(1), period: TimeSpan.FromMinutes(repeatEveryMinute));
    }

    public async Task ReceiveReminder(string reminderName, TickStatus status)
    {
        _logger.Info($"Receive : {reminderName} reminder");

        var feedSourceGrain = _grainFactory.GetGrain<IFeedSource>(0);

        var feedSource = await feedSourceGrain.FindFeedSourceByUrlAsync(reminderName);

        if (feedSource is object)
        {
            _logger.Info($"Fetching : {feedSource.Url}");
            var feedFetcherGrain  = _grainFactory.GetGrain<IFeedFetcher>(feedSource.Url);
            await feedFetcherGrain.FetchAsync(feedSource);
        }
    }
}
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