using Microsoft.Extensions.Hosting;
using Orleans.Hosting;

await Host.CreateDefaultBuilder()
    .UseOrleans(siloBuilder => {
        siloBuilder
            .UseLocalhostClustering()
            .AddMemoryGrainStorageAsDefault()
            .UseTransactions();
    })
    .RunConsoleAsync();