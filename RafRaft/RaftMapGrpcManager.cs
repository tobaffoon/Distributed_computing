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
      private readonly IPEndPoint _endPoint;
      public RaftMapGrpcManager(int port, RaftNodeConfig nodeConfig, IDictionary<int, IPEndPoint> clientsConfig)
      {
         var builder = WebApplication.CreateBuilder();

         // Create server
         _endPoint = new IPEndPoint(IPAddress.Loopback, port);
         builder.Services.AddGrpc();
         builder.WebHost.ConfigureKestrel(options =>
            {
               options.Listen(_endPoint, configure =>
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

            Uri clientUri = new UriBuilder("http", node.Value.Address.ToString(), node.Value.Port).Uri;
            GrpcChannel channel = GrpcChannel.ForAddress(clientUri);
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
         _app.Logger.LogInformation("Server {endPoint} started", _endPoint);
         return serverTask;
      }

      private void Configure()
      {
         _app.Services.GetService<RaftMapGrpcServer>();
      }
   }
}