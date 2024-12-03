namespace RafRaft
{
   using System.Net;
   using Grpc.Net.Client;
   using Microsoft.AspNetCore.Server.Kestrel.Core;
   using RafRaft.Domain;
   using RafRaft.Protos;

   public class RaftMapGrpcManager
   {
      private readonly WebApplication _app;
      private readonly string _address;
      public RaftMapGrpcManager(int port, RaftNodeConfig nodeConfig, IDictionary<int, string> clientsConfig)
      {
         var builder = WebApplication.CreateBuilder();

         // Create server
         _address = $"http://localhost:{port}";
         builder.Services.AddGrpc();
         builder.WebHost.ConfigureKestrel(options =>
            {
               options.Listen(new IPEndPoint(IPAddress.Loopback, port), configure =>
                  {
                     configure.Protocols = HttpProtocols.Http2;
                  });
            });

         // Create clients
         Dictionary<int, RaftMapNode.RaftMapNodeClient> clients = [];
         foreach (var node in clientsConfig)
         {
            if (node.Key == nodeConfig.Id)
            {
               continue;
            }

            GrpcChannel channel = GrpcChannel.ForAddress(_address);
            clients[node.Key] = new RaftMapNode.RaftMapNodeClient(channel);
         }

         builder.Host.ConfigureLogging(logging =>
            {
               logging.ClearProviders();
               logging.AddConsole();
            });

         builder.Services.AddSingleton<IDictionary<int, RaftMapNode.RaftMapNodeClient>>(clients);
         builder.Services.AddSingleton<RaftMapGrpcMediator>();
         builder.Services.AddSingleton(nodeConfig);
         builder.Services.AddSingleton<RaftMapGrpcServer>();

         _app = builder.Build();
         _app.MapGrpcService<RaftMapGrpcServer>();
      }

      public Task Start()
      {
         Configure();
         Task serverTask = _app.RunAsync();
         return serverTask;
      }

      private void Configure()
      {
         _app.Services.GetService<RaftMapGrpcServer>();
      }
   }
}