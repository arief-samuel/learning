using System;
using Microsoft.Extensions.Hosting;

await new HostBuilder()
    .ConfigureServices(services =>
    {
        // services.
    })
    .ConfigureLogging(UriBuilder =>
    {

    })
    .RunConsoleAsync();

    public class HelloWorldClientHostedService : IHostedService
    {
        
    }