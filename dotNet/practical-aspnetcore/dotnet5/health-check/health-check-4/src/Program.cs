using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace practicalaspnetcoreariefs_healthcheck4
{
    public class Program
    {
        public static void Main(string[] args) => CreateHostBuilder(args).Build().Run();

        private static IHostBuilder CreateHostBuilder(string[] args) => 
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webbuilder => {
                    webbuilder.UseStartup<Startup>();
                });
    }

    public class HomeController : Controller
    {

        public ActionResult FakeStatus(int statusCode)
        {
            return StatusCode(statusCode);
        }
        public ActionResult Index()
        {
            return new ContentResult {
                Content = @"
                    <html>
                    <body>
                        <h1>Health Check - Failed/Success Check</h1>
                        This <a href=""/IsUp"">/IsUp</a> always fails at the moment. If you want to see it works, change the following code 
                        <pre>
                            var result = await _client.GetAsync(localServer + ""home/fakestatus/?statusCode=500"");
                        </pre>

                        to 
                        <pre>
                            var result = await _client.GetAsync(localServer + ""/home/fakeStatus/?statusCode=200"")'
                        </pre>
                    </body>
                    </html>",
                ContentType = "text/html"
            };
        }
    }

    public class Startup
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddControllersWithViews();
            services.AddHttpClient<HttpStatusCodehealthCheck>();
            services.AddHealthChecks()
                .AddCheck<HttpStatusCodehealthCheck>("HttpStatusCheck");
            services.AddHttpContextAccessor();
        }

        public void Configure(IApplicationBuilder app)
        {
            app.UseRouting();
            app.UseEndpoints(endpoints => {
                endpoints.MapDefaultControllerRoute();
                endpoints.MapHealthChecks("/IsUp", new HealthCheckOptions
                {
                    ResponseWriter = async (ctx, health) => {
                        if (health.Status == HealthStatus.Healthy)
                            await ctx.Response.WriteAsync($"Everything is good {Environment.NewLine}");
                        foreach (var h in health.Entries)
                        {
                            await ctx.Response.WriteAsync($"{h.Key} {h.Value.Description}");
                        }
                    }
                });
            });
        }
    }

    public class HttpStatusCodehealthCheck : IHealthCheck
    {
        private readonly HttpClient _client;
        private readonly IServer _server;

        public HttpStatusCodehealthCheck(
            HttpClient client,
            IServer server
        )
        {
            _client = client;
            _server = server;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                var serverAddress = _server.Features.Get<IServerAddressesFeature>();
                var localServer = serverAddress.Addresses.First();

                var result = await _client.GetAsync(localServer + "/home/fakestatus/?statuscode=400");

                if (result.StatusCode == HttpStatusCode.OK)
                    return HealthCheckResult.Healthy("Everything is OK\n");

                return HealthCheckResult.Degraded($"Fails: Http Status retuns {result.StatusCode}");
            }
            catch (System.Exception ex)
            {
                return HealthCheckResult.Unhealthy($"Exceptions {ex.Message} : {ex.StackTrace}");
            }
        }
    }

    public class AlwaysBadHealhCheck : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }
    }
}