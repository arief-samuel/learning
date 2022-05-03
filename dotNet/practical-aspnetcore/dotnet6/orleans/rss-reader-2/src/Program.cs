using System.Net;
using Orleans;
using Orleans.Configuration;
using Orleans.Hosting;
using allinterfaces;
using entities;
using grains;

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


