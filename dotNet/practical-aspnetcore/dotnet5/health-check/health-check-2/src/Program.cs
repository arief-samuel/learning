using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace practicalaspnetcoreariefs_healthcheck2
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
                        <h1>Health Check - Custom Message</h1>
                        The health check service on this url <a href=""/whatsUp"">/WhatsUp</a>. /nIt will return `Mucho Bien` thanks to the customized healt message.
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
            services.AddHealthChecks();
        }

        public void Configure(IApplicationBuilder app)
        {
            app.UseRouting();
            app.UseEndpoints(endpoints => {
                endpoints.MapDefaultControllerRoute();
                endpoints.MapHealthChecks("/whatsup", new HealthCheckOptions {
                    ResponseWriter = async (ctx,healt) => {
                        await ctx.Response.WriteAsync("Mucho Bien");
                    }
                });
            });
        }
    }
}