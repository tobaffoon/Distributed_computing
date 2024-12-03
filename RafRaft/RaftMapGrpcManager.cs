namespace RafRaft
{
   using System.Net;
   using Grpc.Core;
   using Grpc.Net.Client;
   using Microsoft.AspNetCore.Server.Kestrel.Core;
   using RafRaft.Domain;
   using RafRaft.Protos;

   public class RaftMapGrpcManager
   {
      private readonly WebApplication _app;
      private readonly RaftGrpcNodeOptions _options;
      private readonly Dictionary<int, string> _clientsAddresses;
      public RaftMapGrpcManager(int port, RaftNodeConfig nodeConfig, RaftGrpcNodeOptions[] clientsConfig)
      {
         var builder = WebApplication.CreateBuilder();

         // Create server
         _options = new RaftGrpcNodeOptions()
         {
            Id = nodeConfig.Id,
            Address = $"http://localhost:{port}"
         };
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
         _clientsAddresses = [];
         foreach (var node in clientsConfig)
         {
            if (node.Id == nodeConfig.Id)
            {
               continue;
            }

            GrpcChannel channel = GrpcChannel.ForAddress(node.Address);
            clients[node.Id] = new RaftMapNode.RaftMapNodeClient(channel);
            _clientsAddresses[node.Id] = node.Address;
         }

         builder.Logging.ClearProviders();
         builder.Logging.AddConsole();

         builder.Services.AddSingleton<IDictionary<int, RaftMapNode.RaftMapNodeClient>>(clients);
         builder.Services.AddSingleton<RaftMapGrpcMediator>();
         builder.Services.AddSingleton(nodeConfig);
         builder.Services.AddSingleton<RaftMapGrpcServer>();

         _app = builder.Build();
         _app.MapGrpcService<RaftMapGrpcServer>();
      }

      public Task Start()
      {
         Task serverTask = _app.RunAsync();
         LaunchClients();
         var server = _app.Services.GetService<RaftMapGrpcServer>();
         server.Start();
         return serverTask;
      }

      private void LaunchClients()
      {
         var clients = _app.Services.GetService<IDictionary<int, RaftMapNode.RaftMapNodeClient>>();
         ConnectToClients(clients);
      }

      private void ConnectToClients(IDictionary<int, RaftMapNode.RaftMapNodeClient> clients)
      {
         List<Task> connectionTasks = [];
         foreach (var nodeAddress in _clientsAddresses)
         {
            Task connectionTask = Task.Run(() => TryConnectToClient(nodeAddress.Key, clients[nodeAddress.Key]));
            connectionTasks.Add(connectionTask);
         }
         Task.WaitAll(connectionTasks);
      }

      private async Task TryConnectToClient(int id, RaftMapNode.RaftMapNodeClient client)
      {
         // TODO move magic numbers to configuration
         for (int i = 1; i <= 5; i++)
         {
            try
            {
               await client.TestConnectionAsync(new Google.Protobuf.WellKnownTypes.Empty());
               return;
            }
            catch (RpcException)
            {
               _app.Logger.LogWarning(@"""Failed to connect to node #{id} at {address}. 
Attempt {i}. Retrying...""",
                  id,
                  _clientsAddresses[id],
                  i
               );

               Thread.Sleep(5000);
            }
         }

         throw new TimeoutException($"Couldn't connect to node #{id} at {_clientsAddresses[id]}");
      }
   }
}