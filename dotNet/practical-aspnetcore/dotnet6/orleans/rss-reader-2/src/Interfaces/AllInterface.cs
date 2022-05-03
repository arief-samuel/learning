using entities;
using Orleans;

namespace allinterfaces
{
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

    public interface IFeedFetcher : IGrainWithStringKey
    {
        Task FetchAsync(FeedSource source);
    }

}