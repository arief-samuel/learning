using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace practicalaspnetcoreariefs_healthcheck3
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
        public ActionResult Index()
        {
            return new ContentResult {
                Content = @"
                    <html>
                    <body>
                        <h1>Health Check - Failed Check</h1>
                        This <a href=""/IsUp"">/IsUp</a> always fails
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
            services.AddHealthChecks()
                .AddCheck<AlwaysBadHealhCheck>("Bad");
        }

        public void Configure(IApplicationBuilder app)
        {
            app.UseRouting();
            app.UseEndpoints(endpoints => {
                endpoints.MapDefaultControllerRoute();
                endpoints.MapHealthChecks("/IsUp");
            });
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