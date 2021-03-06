using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace PracticalAspNetCore
{
    public class Startup
    {
        public void Configure(IApplicationBuilder app, IConfiguration configuration, ILogger<Startup> log)
        {
            app.Run(context =>
            {
                log.LogInformation("This is a information message");
                log.LogDebug("This is debug message");
                return context.Response.WriteAsync(configuration["greeting"]);
            });
        }
    }

    public class Program
    {
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                    webBuilder
                    .ConfigureLogging(builder =>
                    {
                        // Trace, Debug, Information, Warning, Error, Critical, None
                        builder.AddFilter("AppLogger", LogLevel.None);//Pretty much show everything from AppLogger
                        builder.AddFilter("Microsoft", LogLevel.Warning); //Only show Warning log and above from anything that contains Microsoft.
                        builder.AddConsole();
                    })
                    .UseStartup<Startup>()
                );
    }
}