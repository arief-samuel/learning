using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Grpc.Net.Client;

namespace GrpcClient
{
    public class Program
    {
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }

        // Additional configuration is required to successfully run gRPC on macOS.
        // For instructions on how to configure Kestrel and gRPC clients on macOS, visit https://go.microsoft.com/fwlink/?linkid=2099682
        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>()
                    .UseEnvironment(Environments.Development);
                });
    }

    public class Startup
    {
        public void Configure(IApplicationBuilder app){
            //Make sure the grpc server is run
            app.Run(async context => {
                  //we need this switch because we are connecting to an unsecure server,if the server on on SSL.there's no need for this switch.
                  AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport",true);
                  var channel = GrpcChannel.ForAddress("http://localhost:5500");
                  var client = new Billboard.Board.BoardClient(channel);
                  var reply = await client.ShowMessageAsync(new Billboard.MessageRequest
                {
                    Message = "Good morning people of the world",
                    Sender = "Ephra Samuel"
                });
                var displayDate = new DateTime(reply.DisplayTime);
                await context.Response.WriteAsync($"This server sends a gRPC request to a server and get the following result:\nReceived message on {displayDate} from {reply.ReceiveFrom}");
            });
        }
    }
}
