using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;

namespace PracticalAspNetCore
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

    public class Startup
    {
        public void Configure(IApplicationBuilder app){
            app.UseWelcomePage();
        }
    }
}