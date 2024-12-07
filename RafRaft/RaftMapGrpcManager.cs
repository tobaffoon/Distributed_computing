namespace RafRaft
{
   using System.Net;
   using Grpc.Core;
   using Grpc.Net.Client;
   using Microsoft.AspNetCore.Server.Kestrel.Core;
   using Microsoft.Extensions.Logging.Configuration;
   using RafRaft.Domain;
   using RafRaft.Protos;

   public class RaftMapGrpcManager
   {
      private readonly WebApplication _app;
      private readonly RaftGrpcNodeOptions _options;
      private readonly Dictionary<int, string> _clientsAddresses;
      private readonly ILogger _logger;
      private readonly Dictionary<int, RaftMapNode.RaftMapNodeClient> _clients;
      private readonly RaftMapGrpcServer _server;

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
         _clients = [];
         _clientsAddresses = [];
         foreach (var node in clientsConfig)
         {
            if (node.Id == nodeConfig.Id)
            {
               continue;
            }

            GrpcChannel channel = GrpcChannel.ForAddress(node.Address);
            _clients[node.Id] = new RaftMapNode.RaftMapNodeClient(channel);
            _clientsAddresses[node.Id] = node.Address;
         }

         builder.Logging.ClearProviders();
         using ILoggerFactory factory = LoggerFactory.Create(builder => builder
            .AddSimpleConsole(c =>
               {
                  c.TimestampFormat = "[HH:mm:ss] ";
               })
            .SetMinimumLevel(LogLevel.Information));
         _logger = factory.CreateLogger($"RaftNode #{nodeConfig.Id}");

         RaftMapGrpcMediator clientMediator = new RaftMapGrpcMediator(_clients, nodeConfig, _logger);
         _server = new RaftMapGrpcServer(clientMediator, nodeConfig, _logger);
         builder.Services.AddSingleton(_server);

         _app = builder.Build();
         _app.MapGrpcService<RaftMapGrpcServer>();
      }

      public Task Start()
      {
         _logger.LogInformation("1");
         Task serverTask = _app.RunAsync();
         _logger.LogInformation("2");
         ConnectToClients();
         _logger.LogInformation("3");
         _server.Start();
         return serverTask;
      }

      private void ConnectToClients()
      {
         List<Task> connectionTasks = [];
         foreach (var nodeAddress in _clientsAddresses)
         {
            Task connectionTask = Task.Run(() => TryConnectToClient(nodeAddress.Key, _clients[nodeAddress.Key]));
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
               _logger.LogWarning(@"""Failed to connect to node #{id} at {address}. 
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