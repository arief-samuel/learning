using System.Xml;
using allinterfaces;
using entities;
using Microsoft.SyndicationFeed;
using Microsoft.SyndicationFeed.Atom;
using Microsoft.SyndicationFeed.Rss;
using Orleans;
using Orleans.Runtime;

namespace grains
{
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

}