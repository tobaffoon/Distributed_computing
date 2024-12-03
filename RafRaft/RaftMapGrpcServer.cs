namespace RafRaft
{
   using System.Net;
   using Grpc.Net.Client;
   using Microsoft.AspNetCore.Server.Kestrel.Core;
   using RafRaft.Domain;
   using RafRaft.Protos;

   public class RaftMapGrpcServer
   {
      private readonly WebApplication _app;
      private readonly IPEndPoint _endPoint;
      public RaftMapGrpcServer(int port, RaftNodeConfig nodeConfig, IDictionary<int, IPEndPoint> clientsConfig)
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

         _app = builder.Build();
         _app.MapGrpcService<RaftMapGrpcService>();
      }

      public Task Start()
      {
         Task serverTask = _app.RunAsync();
         _app.Logger.LogInformation("Server {endPoint} started", _endPoint);
         return serverTask;
      }
   }
}