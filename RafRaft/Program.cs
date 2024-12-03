namespace RafRaft
{
   using System.Net;
   using RafRaft.Domain;

   class Program
   {
      static async Task Main(string[] args)
      {
         HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

         builder.Configuration.Sources.Clear();

         IHostEnvironment env = builder.Environment;

         builder.Configuration
             .AddJsonFile(args[0], optional: false, reloadOnChange: false);

         var id = builder.Configuration.GetValue<int>("Id");
         var port = builder.Configuration.GetValue<int>("Port");
         var peers = builder.Configuration.GetSection("Peers").Get<RaftGrpcNodeOptions[]>();

         System.Console.WriteLine($"{id}, {port}");
         foreach (RaftGrpcNodeOptions node in peers)
         {
            System.Console.WriteLine($"{node.Id}, {node.Address}");
         }
         // List<int> ports = [5021, 5022, 5023, 5024, 5025];
         // List<int> ids = [0, 1, 2, 3, 4];

         // Dictionary<int, IPEndPoint> clientsConfig = [];
         // for (int i = 0; i < ports.Count; i++)
         // {
         //    clientsConfig[ids[i]] = new IPEndPoint(IPAddress.Loopback, ports[i]);
         // }

         // RaftNodeConfig nodeConfig = new RaftNodeConfig(ids[0], 1000, 100, ids);
         // RaftMapGrpcManager server = new RaftMapGrpcManager(ports[0], nodeConfig, clientsConfig);
         // await server.Start();
      }
   }
}
