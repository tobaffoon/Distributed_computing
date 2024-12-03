namespace RafRaft
{
   using System.Net;
   using RafRaft.Domain;

   class Program
   {
      static async Task Main(string[] args)
      {
         List<int> ports = [5021, 5022, 5023, 5024, 5025];
         List<int> ids = [0, 1, 2, 3, 4];

         Dictionary<int, IPEndPoint> clientsConfig = [];
         for (int i = 0; i < ports.Count; i++)
         {
            clientsConfig[ids[i]] = new IPEndPoint(IPAddress.Loopback, ports[i]);
         }

         RaftNodeConfig nodeConfig = new RaftNodeConfig(ids[0], 1000, 100, ids);
         RaftMapGrpcManager server = new RaftMapGrpcManager(ports[0], nodeConfig, clientsConfig);
         await server.Start();
      }
   }
}
