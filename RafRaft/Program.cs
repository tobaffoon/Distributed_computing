using System.Net;

namespace RafRaft
{
  class Program
  {
    static async Task Main(string[] args)
    {
      List<int> ports = [5021, 5022, 5023, 5024, 5025];
      foreach (int port in ports)
      {
        // new RaftGrpcNode<int>(new IPEndPoint(IPAddress.Loopback, port), 10, 10, ports);
      }
    }
  }
}
