using System.Net;
using System.Text.Json.Serialization;
using Orleans;
using Orleans.Configuration;
using Orleans.Hosting;

var builder = WebApplication.CreateBuilder();
builder.Services.AddHttpClient();
builder.Logging.SetMinimumLevel(LogLevel.Information).AddConsole();
builder.Host.UseOrleans(orleans => {
    orleans
        .UseLocalhostClustering()
        .UseInMemoryReminderService()
        .Configure<ClusterOptions>(options => {
            options.ClusterId = "dev";
            options.ServiceId = "http-client";
        })
        .Configure<EndpointOptions>(options => options.AdvertisedIPAddress = IPAddress.Loopback)
        .ConfigureApplicationParts(parts => parts.AddApplicationPart(typeof(TimeKeeperGrain).Assembly).WithReferences());
});
var app = builder.Build();

app.MapGet("/", async context => {
    var client = context.RequestServices.GetService<IGrainFactory>()!;
    var timeZone = "Africa/Cairo";
    var grain = client.GetGrain<ITimeKeeper>(timeZone)!;
    var localTime = await grain.GetCurrentTime(timeZone);

    await context.Response.WriteAsync(@$"
            <html>
                <head>
                    <link rel=""stylesheet"" href=""https://cdn.jsdelivr.net/npm/uikit@3.5.5/dist/css/uikit.min.css"">
                </head>
                <body>
                    <h1>Local time in {localTime.timeZone} is {localTime.dateTime}</h1>
                </body>
            </html>
    ");
});
app.Run();
public class TimeKeeperGrain : Grain, ITimeKeeper
{
    private readonly ILogger _log;
    private readonly IHttpClientFactory _httpFactory;
    const string url = "http://worldtimeapi.org/api/timezone/";

    public TimeKeeperGrain(
        ILogger<TimeKeeperGrain> log,
        IHttpClientFactory httpFactory
    )
    {
        _log = log;
        _httpFactory = httpFactory;
    }
    public async Task<(DateTimeOffset dateTime, string timeZone)> GetCurrentTime(string timeZone)
    {
        var client = _httpFactory.CreateClient();

        var result = await client.GetAsync(url + timeZone);
        var worldClock = await result.Content.ReadFromJsonAsync<WorldTime>();
        
        return (worldClock!.DateTime, timeZone);
    }
}
public class WorldTime
{
    [JsonPropertyName("datetime")]
    public DateTimeOffset DateTime { get; set; }
}
public interface ITimeKeeper : IGrainWithStringKey
{
    Task<(DateTimeOffset dateTime, string timeZone)> GetCurrentTime(string timeZone);
}