using System.Net;
using RafRaft.Protos;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Grpc.Net.Client;
using RaftRaft.Services;

namespace RafRaft
{
  class Program
  {
    static async Task Main(string[] args)
    {
      // List<RaftGrpcNode>
      var builder = WebApplication.CreateBuilder(args);

      // Add services to the container.
      builder.Services.AddGrpc();
      builder.WebHost.ConfigureKestrel(options =>
            {
              options.Listen(IPAddress.Any, 5021, configure =>
              {
                configure.Protocols = HttpProtocols.Http2;
              });
            });

      var app = builder.Build();
      app.MapGrpcService<RaftGrpcService>();
      var serverTask = app.RunAsync();

      // client
      var input = new VoteRequest
      {
        Term = 1,
        CandidateId = 1,
        LastLogId = 1,
        LastLogTerm = 1
      };
      var channel = GrpcChannel.ForAddress("http://localhost:5021");
      var client = new RaftNode.RaftNodeClient(channel);
      var reply = await client.RequestVoteAsync(input);
      Console.WriteLine(reply.VoteGranted);

      await serverTask;
    }
  }
}
