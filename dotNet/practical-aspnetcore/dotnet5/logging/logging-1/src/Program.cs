using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PracticalAspNetCoreAriefs
{
    class Program
    {
        static void Main(string[] args) => CreateHostBuilder(args).Build().Run();

        private static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder => 
                    webBuilder
                        .ConfigureLogging(builder => {
                            builder.SetMinimumLevel(LogLevel.Trace);
                            builder.AddConsole();
                        })
                        .UseStartup<Startup>()
                );
    }

    public class Startup
    {
        public void Configure(IApplicationBuilder app, ILogger<Startup> log){
            app.Run(context => {
                log.LogTrace("Trace message");
                log.LogDebug("Debug message");
                log.LogInformation("Information message");
                log.LogWarning("Warning message");
                log.LogError("Error message");
                log.LogCritical("Ciritcal message");
                return context.Response.WriteAsync("Hello world. Take a look at your terminal to see the logging messages.");
            });
        }
    }
}
