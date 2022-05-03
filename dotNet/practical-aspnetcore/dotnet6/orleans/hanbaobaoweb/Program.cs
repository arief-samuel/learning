using System.Data.Common;
using System.Diagnostics;
using System.Net;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Orleans;
using Orleans.Configuration;
using Orleans.Hosting;
using Orleans.Runtime;

var builder = WebApplication.CreateBuilder();
builder.Services.AddHttpClient();
builder.Services.AddControllers();
builder.Services.AddSingleton<ReferenceDataService>();
builder.Logging.SetMinimumLevel(LogLevel.Information).AddConsole();
builder.Host.UseOrleans(orleans => {
    orleans
        .UseLocalhostClustering()
        .AddMemoryGrainStorage("definitions")
        .UseInMemoryReminderService()
        .Configure<ClusterOptions>(options => {
            options.ClusterId = "dev";
            options.ServiceId = "http-client";
        })
        .Configure<EndpointOptions>(options => options.AdvertisedIPAddress = IPAddress.Loopback)
        .ConfigureApplicationParts(parts => parts.AddApplicationPart(typeof(DictionaryEntryGrain).Assembly).WithReferences());
});
var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();
app.UseEndpoints(endpoints => {
    endpoints.MapControllers();
});
app.MapGet("/timezone", async context => {
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

public class DictionaryEntryGrain : Grain, IDictionaryEntryGrain
{
    private readonly IPersistentState<DictionaryEntryState> _state;
    private ReferenceDataService _referenceDataService;

    public DictionaryEntryGrain(
        // Inject some strage. We will use the `definitions` 
        // storage provider configure in Program.cs
        // and we will call this piece of state `def`, to distinguish it from 
        // any other state we might want to have.
        [PersistentState(stateName:"defs", storageName: "definitions")]
        IPersistentState<DictionaryEntryState> defs,
        ReferenceDataService referenceDataService
    )
    {
        _state = defs;
        _referenceDataService = referenceDataService;
    }

    public override async Task OnActivateAsync()
    {
        // If there is no state saved for this entry yet,
        // load the state from the referencedictionary and store it.
        if (_state.State?.Definition is null)
        {
            // Find the definition from the reference data, using this grain's id to look it up
            var headword = this.GetPrimaryKeyString();
            var result = await _referenceDataService.QueryByHeadwordAsync(headword);
            if (result is {Count: > 0 } && result.FirstOrDefault() is TermDefinition definition)
            {
                _state.State.Definition = definition;

                // Write the state but don't wait for completion.
                // If fails, we eill write it next time.
                _state.WriteStateAsync().Ignore();
            }
        }
    }
    public Task<TermDefinition> GetDefinitionAsync() => Task.FromResult(_state.State.Definition);

    public async Task UpdateDefinitionAsync(TermDefinition value)
    {
        if (!string.Equals(value.Simplified, this.GetPrimaryKeyString(), StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Cannot change the headword for a definition");
        }
        _state.State.Definition = value;
        await _state.WriteStateAsync();
    }   
}

/// <summary>
/// This grain demonstrates a simple wa to throttle a given client (identified by their IP address, which is used the primary key of the grain)
/// It uses a call filter to maintain a count of recent call and throttles if they exceed a defined threshold. The score decays over time untill, allowing 
/// the client to resume making calls
/// </summary>
public class UserAgentGrain : Grain, IUserAgentGrain, IIncomingGrainCallFilter
{
    private readonly IGrainFactory _grainFactory;
    private readonly ILogger _logger;
    private Stopwatch _stopwatch = new Stopwatch();
    private double _throttleScore;
    private const int ThrottleThreshold = 3;
    private const int DecayPeriod = 5;
    private const double DecayRate = (double)ThrottleThreshold / (double)DecayPeriod;

    public UserAgentGrain(
        IGrainFactory grainFactory,
        ILogger<UserAgentGrain> logger
    )
    {
        _grainFactory =grainFactory;
        _logger = logger;
    }
    public Task<TermDefinition> GetDefinitionAsync(string id) => _grainFactory.GetGrain<IDictionaryEntryGrain>(id).GetDefinitionAsync();
    public Task<List<TermDefinition>> GetSearchResultAsync(string query) => _grainFactory.GetGrain<ISearchGrain>(query).GetSearchResultAsync();
    public Task UpdateDefinitionAsync(string id, TermDefinition value) => _grainFactory.GetGrain<IDictionaryEntryGrain>(id).UpdateDefinitionAsync(value);

    public async Task Invoke(IIncomingGrainCallContext context)
    {
        if (context.Arguments?.FirstOrDefault() is string phrase && string.Equals("please", phrase))
            _throttleScore = 0;
        
        // Work out how long it's been since the last call
        var elapsedSeconds = _stopwatch.Elapsed.TotalSeconds;
        _stopwatch.Restart();

        // Calculate a new score based on constant rate of score decay and the time which elapsed since the last call.
        _throttleScore = Math.Max(0, _throttleScore - elapsedSeconds * DecayRate) + 1;

        // If the user has exceeded the threshold, deny their request and give them a helpful warning.
        if (_throttleScore > ThrottleThreshold)
        {
            var remainingSeconds = Math.Max(0, (int)Math.Ceiling((_throttleScore - (ThrottleThreshold - 1)) / DecayRate));
            _logger.LogError("Throttling");
            throw new ThrottlingException($"Request rate exceed, wait {remainingSeconds}s before retrying");
        }
        await context.Invoke();
    }

}

public class ReferenceDataService
{
    private readonly string _connectionString;
    private readonly TaskScheduler _scheduler;
    private const string Ordering = "hsk_level not null desc, hsk_level asc, part_of_speech not null desc, frequency desc";
    public ReferenceDataService()
    {
        var builder = new SqliteConnectionStringBuilder("Data Source=hanbaobao.db;")
        {
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Shared,
        };
        _connectionString = builder.ToString();
        _scheduler = new ConcurrentExclusiveSchedulerPair(
            TaskScheduler.Default,
            maxConcurrencyLevel: 1
        ).ExclusiveScheduler;
    }
    public Task<List<TermDefinition>> QueryByHeadwordAsync(string query)
    {
        return Task.Factory.StartNew(() => {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            var cmd = new SqliteCommand("SELECT * FROM DICTIONARY WHERE SIMPLIFIED=$term or TRADITIONAL=$term ORDER BY " + Ordering, connection);
            cmd.Parameters.AddWithValue("$term", query);
            cmd.Prepare();

            var reader = cmd.ExecuteReader();
            return ReadAllAsTermDefinition(reader);
        },CancellationToken.None,TaskCreationOptions.RunContinuationsAsynchronously,_scheduler);
    }
    public List<TermDefinition> ReadAllAsTermDefinition(DbDataReader reader)
    {
        int rowIdColId = reader.GetOrdinal("rowid");
        int simplifiedColId = reader.GetOrdinal("simplified");
        int traditionalColId = reader.GetOrdinal("traditional");
        int pinyinColId = reader.GetOrdinal("pinyin");
        int definitionColId = reader.GetOrdinal("definition");
        int hskLevelColId = reader.GetOrdinal("hsk_level");
        int classifierColId = reader.GetOrdinal("classifier");
        int posColId = reader.GetOrdinal("part_of_speech");
        int frequencyColId = reader.GetOrdinal("frequency");
        int conceptColId = reader.GetOrdinal("concept");
        int topicColId = reader.GetOrdinal("topic");
        int parentTopicColId = reader.GetOrdinal("parent_topic");
        int notesColId = reader.GetOrdinal("notes");

        var results = new List<TermDefinition>();
        while(reader.Read())
        {
            var result = new TermDefinition();
            result.Id = reader.GetInt64(rowIdColId);
            result.Simplified = reader.IsDBNull(simplifiedColId) ? null : reader.GetString(simplifiedColId);
            result.Traditional = reader.IsDBNull(traditionalColId) ? null : reader.GetString(traditionalColId);
            result.Pinyin = reader.IsDBNull(pinyinColId) ? null : reader.GetString(pinyinColId);
            result.Definition = reader.IsDBNull(definitionColId) ? null : reader.GetString(definitionColId);
            result.Classifier = reader.IsDBNull(classifierColId) ? null : reader.GetString(classifierColId);
            result.HskLevel = reader.IsDBNull(hskLevelColId) ? 0 : reader.GetInt32(hskLevelColId);
            result.PartOfSpeech = reader.IsDBNull(posColId) ? null : PartOfSpeechToStrings(reader.GetInt64(posColId));
            result.Frequency = reader.IsDBNull(frequencyColId) ? 0 : reader.GetDouble(frequencyColId);
            result.Concept = reader.IsDBNull(conceptColId) ? null : reader.GetString(conceptColId);
            result.Topic = reader.IsDBNull(topicColId) ? null : reader.GetString(topicColId);
            result.ParentTopic = reader.IsDBNull(parentTopicColId) ? null :reader.GetString(parentTopicColId);
            result.Notes = reader.IsDBNull(notesColId) ? null : reader.GetString(notesColId);
            results.Add(result);
        }
        return results;
    }
    public static List<string> PartOfSpeechToStrings(long input)
    {
        List<string> result = new List<string>();
        if ((input & ((long)1)) != 0)
            result.Add("ADDRESS");
        if ((input & (((long)1 << 1))) != 0)
            result.Add("ADJECTIVE");
        if ((input & ((long)1 << 2 )) != 0)
            result.Add("ADVERB");
        if ((input & ((long)1 << 3 )) != 0)
            result.Add("AUXILIARY VERB");
        if ((input & ((long)1 << 4 )) != 0)
            result.Add("BOUND MORPHENE");
        if ((input & ((long)1 << 5 )) != 0)
            result.Add("SET PHRASE");
        if ((input & ((long)1 << 6 )) != 0)
            result.Add("CITY");
        if ((input & ((long)1 << 7 )) != 0)
            result.Add("COMPLEMENT");
        if ((input & ((long)1 << 8 )) != 0)
            result.Add("CONJUNCTION");
        if ((input & ((long)1 << 9 )) != 0)
            result.Add("COUNTRY");
        if ((input & ((long)1 << 10 )) != 0)
            result.Add("DATE");
        if ((input & ((long)1 << 11 )) != 0)
            result.Add("DETERMINER");
        if ((input & ((long)1 << 12 )) != 0)
            result.Add("DIRECTIONAL");
        if ((input & ((long)1 << 13 )) != 0)
            result.Add("EXPRESSION");
        if ((input & ((long)1 << 14 )) != 0)
            result.Add("FOREIGN TERM");
        if ((input & ((long)1 << 15 )) != 0)
            result.Add("GEOGRAPHY");
        if ((input & ((long)1 << 16 )) != 0)
            result.Add("IDIOM");
        if ((input & ((long)1 << 17 )) != 0)
            result.Add("INTERJECTION");
        if ((input & ((long)1 << 18 )) != 0)
            result.Add("MEASURE WORD");
        if ((input & ((long)1 << 19 )) != 0)
            result.Add("MEASUREMENT");
        //if ((input & ((long)1 << 20)) != 0) result.Add("NAME");
        if ((input & ((long)1 << 20 )) != 0)
            result.Add("NOUN");
        if ((input & ((long)1 << 21 )) != 0)
            result.Add("NOUN");
        if ((input & ((long)1 << 22 )) != 0)
            result.Add("NUMBER");
        if ((input & ((long)1 << 23 )) != 0)
            result.Add("NUMERAL");
        if ((input & ((long)1 << 24 )) != 0)
            result.Add("ONOMATOPOEIA");
        if ((input & ((long)1 << 25 )) != 0)
            result.Add("ORDINAL");
        if ((input & ((long)1 << 26 )) != 0)
            result.Add("ORGANIZATION");
        if ((input & ((long)1 << 27 )) != 0)
            result.Add("PARTICLE");
        if ((input & ((long)1 << 28 )) != 0)
            result.Add("PERSON");
        if ((input & ((long)1 << 29 )) != 0)
            result.Add("PHONETIC");
        if ((input & ((long)1 << 30 )) != 0)
            result.Add("PHRASE");
        if ((input & ((long)1 << 31 )) != 0)
            result.Add("PLACE");
        if ((input & ((long)1 << 32 )) != 0)
            result.Add("PREFIX");
        if ((input & ((long)1 << 33 )) != 0)
            result.Add("PREPOSITION");
        if ((input & ((long)1 << 34 )) != 0)
            result.Add("PRONOUN");
        if ((input & ((long)1 << 35 )) != 0)
            result.Add("PROPER NOUN");
        if ((input & ((long)1 << 36 )) != 0)
            result.Add("QUANTITY");
        if ((input & ((long)1 << 37 )) != 0)
            result.Add("RADICAL");
        if ((input & ((long)1 << 38 )) != 0)
            result.Add("SUFFIX");
        if ((input & ((long)1 << 39 )) != 0)
            result.Add("TEMPORAL");
        if ((input & ((long)1 << 40 )) != 0)
            result.Add("TIME");
        if ((input & ((long)1 << 41 )) != 0)
            result.Add("VERB");
        return result;
    }
    public Task<List<string>> QueryHeadwordsByAnyAsync(string query)
    {
        // Why are we scheduling the database call on a specific TaskScheduler?
        // The answer is centered around SQLite being a synchronous library and performs blocking IO operations.
        // There are asynchronous pverloads, but they call the synchronous methods internally.
        // By running many blocking IO operations on the .NET Thread Pool in parallel, we can starve the pool and cause severe performance
        // degradation, which can result in a denial of service.
        // A better approach for handling a synchronous library like this would be to have a dedicated pool of threads, rather than borrowing
        // ThreadPool threads like this implementation does.
        return Task.Factory.StartNew(() => {
            if (IsProbablyCjk(query))
                return QueryHeadwordByHeadword(query);
            
            return QueryHeadwordByDefinition(query);
            
        },
        CancellationToken.None, 
        TaskCreationOptions.RunContinuationsAsynchronously,
        _scheduler);
    }
    public Task<List<TermDefinition>> QueryByAnySync(string query)
    {
        if (IsProbablyCjk(query))
            return QueryByHeadwordAsync(query);

        return Task.Factory.StartNew(() => {
            return QueryByDefinition(query);
        },
        CancellationToken.None,
        TaskCreationOptions.RunContinuationsAsynchronously,
        _scheduler);
    }
    private List<TermDefinition> QueryByDefinition(string query)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        
        using var cmd = new SqliteCommand("select * from dictionary where rowid in (select rowid from fts_definition where fts_definition match $query) order by " + Ordering, connection);
        cmd.Parameters.AddWithValue("$term", query);
        
        var reader = cmd.ExecuteReader(System.Data.CommandBehavior.CloseConnection);
        return ReadAllAsTermDefinition(reader);
    }
    private List<string> QueryHeadwordByDefinition(string query)
    {
        using var connection = new SqliteConnection(_connectionString);
        using var cmd = new SqliteCommand("select distinct simplified from dictionary where rowid in (select rowid from fts_definition where fts_definition match $query) order by " + Ordering, connection);
            
        connection.Open();
        cmd.Parameters.AddWithValue("$query", query);
        var reader = cmd.ExecuteReader(System.Data.CommandBehavior.CloseConnection);
        return ReadHeadwords(reader);
    }
    private List<string> QueryHeadwordByHeadword(string query)
    {
        using var connection = new SqliteConnection(_connectionString);
        using var cmd = new SqliteCommand("select distinct simplified from dictionary where simplified=$term or traditional=$term order by " + Ordering, connection);
        connection.Open();
        cmd.Parameters.AddWithValue("$query", query);

        var reader = cmd.ExecuteReader();
        return ReadHeadwords(reader);
    }
    private List<string> ReadHeadwords(DbDataReader reader)
    {
        int simplifiedColId = reader.GetOrdinal("simplified");
        var results = new List<string>();
        while (reader.Read())
        {
            if (!reader.IsDBNull(simplifiedColId))
                results.Add(reader.GetString(simplifiedColId));
        }

        return results;
    }
    private bool IsProbablyCjk(string text)
    {
        foreach (var c in text)
        {
            var val = (int)c;
            if (IsInRange(UnicodeRanges.CjkUnifiedIdeographs, c) || IsInRange(UnicodeRanges.CjkUnifiedIdeographsExtensionA, c))
            {
                return true;
            }
        }
        return false;

        static bool IsInRange(UnicodeRange range, char c)
        {
            var val = (int)c;
            return val >= range.FirstCodePoint && val <= (range.FirstCodePoint + range.Length);
        }
    }
}
public class SearchGrain : Grain, ISearchGrain
{
    private readonly ReferenceDataService _searchDatabase;
    private readonly IGrainFactory _grainFactory;
    private List<TermDefinition> _cacheResult;
    private Stopwatch _timeSinceLastUpdate = new Stopwatch();

    public SearchGrain(
        ReferenceDataService searchDatabase,
        IGrainFactory grainFactory
    )
    {
        _searchDatabase = searchDatabase;
        _grainFactory = grainFactory;
    }
    public async Task<List<TermDefinition>> GetSearchResultAsync()
    {
        //If the query has already been performed, return the result from the cache.
        if (_cacheResult is object && _timeSinceLastUpdate.Elapsed < TimeSpan.FromSeconds(10))
            return _cacheResult;
        
        // This grain is keyed on the search query, so use that to search
        var query = this.GetPrimaryKeyString();

        // Search for possible matches from the full text search database
        var headwords = await _searchDatabase.QueryHeadwordsByAnyAsync(query);

        // Fan out and get all of the definitions for each matching headword
        var tasks = new List<Task<TermDefinition>>();
        foreach (var headword in headwords.Take(25))
        {
            var entryGrain = _grainFactory.GetGrain<IDictionaryEntryGrain>(headword);
            tasks.Add(entryGrain.GetDefinitionAsync());
        }

        // Wait for all calss to complete
        await Task.WhenAll(tasks);

        // Collect the results into a list to return
        var results = new List<TermDefinition>(tasks.Count);
        foreach (var task in tasks)
        {
            results.Add(await task);
        }

        // Cache the result for next time
        _cacheResult = results;
        _timeSinceLastUpdate.Restart();

        return results;
    }
}
public interface ITimeKeeper : IGrainWithStringKey
{
    Task<(DateTimeOffset dateTime, string timeZone)> GetCurrentTime(string timeZone);
}
public interface IDictionaryEntryGrain : IGrainWithStringKey
{
    Task<TermDefinition> GetDefinitionAsync();
    Task UpdateDefinitionAsync(TermDefinition value);
}
public interface ISearchGrain : IGrainWithStringKey
{
    Task<List<TermDefinition>> GetSearchResultAsync();
}
public interface IUserAgentGrain : IGrainWithStringKey
{
    Task<List<TermDefinition>> GetSearchResultAsync(string query);
    Task<TermDefinition> GetDefinitionAsync(string id);
    Task UpdateDefinitionAsync(string id, TermDefinition value);
}

[Serializable]
internal class ThrottlingException : Exception
{
    public ThrottlingException() { }
    public ThrottlingException(string? message) : base(message) { }
    public ThrottlingException(string? message, Exception? innerException) : base(message, innerException) { }
    protected ThrottlingException(SerializationInfo info, StreamingContext context) : base(info, context) { }
}

[Serializable]
public class TermDefinition
{
    public long Id { get;  set; }
    public string? Simplified { get;  set; }
    public string? Traditional { get;  set; }
    public string? Pinyin { get;  set; }
    public string? Definition { get;  set; }
    public string? Classifier { get;  set; }
    public int HskLevel { get;  set; }
    public List<string>? PartOfSpeech { get;  set; }
    public string? Notes { get; internal set; }
    public string? ParentTopic { get;  set; }
    public string? Concept { get;  set; }
    public double Frequency { get;  set; }
    public string? Topic { get;  set; }
}

[Serializable]
public class DictionaryEntryState
{
    public TermDefinition? Definition { get;  set; }
}
public class WorldTime
{
    [JsonPropertyName("datetime")]
    public DateTimeOffset DateTime { get; set; }
}

[ApiController]
[Route("api/[controller]")]
public class EntryController : ControllerBase
{
    private readonly IGrainFactory _grainFactory;

    public EntryController(
        IGrainFactory grainFactory
    )
    {
        _grainFactory = grainFactory;
    }

    [HttpGet]
    public async Task<IActionResult> Get(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return BadRequest("Provided an invalid id");
        
        var entryGrain = _grainFactory.GetGrain<IDictionaryEntryGrain>(id)!;
        var result = await entryGrain.GetDefinitionAsync();
        return Ok(result);
    }
    
    [HttpPost]
    public async Task<IActionResult> Post([FromBody]TermDefinition entry)
    {
        if (string.IsNullOrWhiteSpace(entry?.Simplified))
            return BadRequest("provided an invalid entry");
        
        var entryGrain = _grainFactory.GetGrain<IDictionaryEntryGrain>(entry.Simplified);
        await entryGrain.UpdateDefinitionAsync(entry);
        return Ok();
    }
}

[ApiController]
[Route("api/[controller]")]
public class SearchController : ControllerBase
{
    private readonly IGrainFactory _grainFactory;

    public SearchController(
        IGrainFactory grainFactory
    )
    {
        _grainFactory = grainFactory;
    }

    [HttpGet]
    public async Task<IActionResult> GetByQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return BadRequest("Provided an empty query");
        
        // We Implementsa throttling systems by creating a grain for each client, keyed by their IP.
        // All calls made by the client go through that grain. The grain monitors it's own request rate
        // and denies requests if they exceed some definedd request rate.
        var clientId = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
        var userAgentGrain = _grainFactory.GetGrain<IUserAgentGrain>(clientId);
        try
        {
            var results = await userAgentGrain.GetSearchResultAsync(query);
            return Ok(results);
        }
        catch (ThrottlingException exc)
        {
           return StatusCode(429, exc.Message);
        } 
    }
}