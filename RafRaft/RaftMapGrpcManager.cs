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
      private readonly ILogger _logger;
      private readonly Dictionary<int, RaftMapNode.RaftMapNodeClient> _clients;
      private readonly RaftMapGrpcServer _server;
      private readonly Dictionary<int, bool> _startupAvailablePeers;
      private int temp_port;
      public RaftMapGrpcManager(int port, RaftNodeConfig nodeConfig, RaftGrpcNodeOptions[] clientsConfig)
      {
         temp_port = port;

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
         _startupAvailablePeers = [];

         // Setup logging
         builder.Logging.ClearProviders();
         using ILoggerFactory factory = LoggerFactory.Create(builder => builder
            .AddSimpleConsole(c =>
               {
                  c.TimestampFormat = "[HH:mm:ss:fffff] ";
               })
            .SetMinimumLevel(LogLevel.Trace));
         _logger = factory.CreateLogger($"RaftNode #{nodeConfig.Id}");

         Dictionary<int, GrpcChannel> _channels = [];
         foreach (var node in clientsConfig)
         {
            if (node.Id == nodeConfig.Id)
            {
               continue;
            }

            GrpcChannel channel = GrpcChannel.ForAddress(node.Address);
            try
            {
               channel.ConnectAsync();
            }
            catch (Exception e)
            {
               _logger.LogCritical(e.Message);
            }
            _logger.LogCritical(channel.State.ToString()); // save channels - check what is their status on each call/ Maybe change clients dictionary

            _clients[node.Id] = new RaftMapNode.RaftMapNodeClient(channel);
            _channels[node.Id] = channel;

            _startupAvailablePeers[node.Id] = true;
         }

         RaftMapGrpcMediator clientMediator = new RaftMapGrpcMediator(_clients, _channels, nodeConfig, _logger);
         _server = new RaftMapGrpcServer(clientMediator, nodeConfig, _logger);
         builder.Services.AddSingleton(_server);


         _app = builder.Build();
         _app.MapGrpcService<RaftMapGrpcServer>();
      }

      public Task Start()
      {
         Task serverTask = _app.RunAsync();
         ConnectToClients();
         _server.Start(_startupAvailablePeers);
         return serverTask;
      }

      private void ConnectToClients()
      {
         List<Task> connectionTasks = [];
         foreach (var nodeConfig in _clients)
         {
            Task<bool> connectionTask = TryConnectToClient(nodeConfig.Key, nodeConfig.Value);
            Task markInactiveTask = connectionTask.ContinueWith(task =>
            {
               if (task.Result == false)
               {
                  _startupAvailablePeers[nodeConfig.Key] = false;
               }
            });
            connectionTasks.Add(connectionTask);
            connectionTasks.Add(markInactiveTask);
         }
         Task.WaitAll(connectionTasks);
      }

      private async Task<bool> TryConnectToClient(int id, RaftMapNode.RaftMapNodeClient client)
      {
         // wait fixed amout (5 sec) => connect to nodes that can be connected to and consider only them. Mark others as inactive
         try
         {
            await client.TestConnectionAsync(new Google.Protobuf.WellKnownTypes.Empty());
            return true;
         }
         catch (RpcException)
         {
            _logger.LogWarning(@"""Couldn't connect to node #{id}
            It will be marked inactive""", id);
            return false;
         }

         // TODO move magic numbers to configuration
         //          for (int i = 1; i <= 5; i++)
         //          {
         //             try
         //             {
         //                client.TestConnection(new Google.Protobuf.WellKnownTypes.Empty());
         //                return;
         //             }
         //             catch (RpcException)
         //             {
         //                _logger.LogWarning(@"""Failed to connect to node #{id} at {address}. 
         // Attempt {i}. Retrying...""",
         //                   id,
         //                   _clientsAddresses[id],
         //                   i
         //                );
         //                // wait fixed amout (5 sec) => connect to nodes that can be connected to and consider only them. Mark others as inactive
         //                // don't sleep: if request comes while sleeping => no reply => marked as inactive
         //             }
         //          }

         //          throw new TimeoutException($"Couldn't connect to node #{id} at {_clientsAddresses[id]}");
      }
   }
}
