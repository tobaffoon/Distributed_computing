namespace RafRaft
{
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

         int id = builder.Configuration.GetValue<int>("Id");
         int port = builder.Configuration.GetValue<int>("Port");
         RaftGrpcNodeOptions[] nodes = builder.Configuration.GetSection("Peers").Get<RaftGrpcNodeOptions[]>()!;

         RaftNodeConfig nodeConfig = new RaftNodeConfig(id, 100, 450, 550, nodes.Select(grpcOptions => grpcOptions.Id).ToList());
         RaftMapGrpcManager manager = new RaftMapGrpcManager(port, nodeConfig, nodes);
         await manager.Start();
      }
   }
}
