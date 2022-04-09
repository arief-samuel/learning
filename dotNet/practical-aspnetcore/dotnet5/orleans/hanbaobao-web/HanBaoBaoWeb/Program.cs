using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans.Hosting;
using System;

await Host.CreateDefaultBuilder(args)
    .UseOrleans((ctx, siloBuilder) => {
        if (ctx.HostingEnvironment.IsDevelopment())
        {
            siloBuilder.UseLocalhostClustering();
            //siloBuilder.AddMemoryGrainStorage("definitions");
        }
        else
        {
           
        }
    })
    .ConfigureWebHostDefaults(webBuilder => {
        webBuilder.ConfigureServices(services => services.AddController());
        webBuilder.Configure((ctx,app) => {
            if (ctx.HostingEnvironment.IsDevelopment())
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
        });
    })
    .ConfigureServices(services => {
        services.AddSingleton<ReferenceDataService>();
    })
    .RunConsoleAsync();