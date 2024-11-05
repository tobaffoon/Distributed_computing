using System.Net;
using RafRaft.Protos;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Grpc.Net.Client;
using RaftRaft.Services;
using RafRaft.Domain;

namespace RafRaft;

public abstract class RaftGrpcNode<T> : RaftListNode<T> where T : new()
{
  private readonly RaftNode.RaftNodeClient client;

  public RaftGrpcNode(IPEndPoint EndPoint, long BroadcastTime, long ElectionTimeout, IEnumerable<int> NodeIds) : base(BroadcastTime, ElectionTimeout, NodeIds)
  {
    var builder = WebApplication.CreateBuilder();

    // Create server
    builder.Services.AddGrpc();
    builder.WebHost.ConfigureKestrel(options =>
          {
            options.Listen(EndPoint.Address, EndPoint.Port, configure =>
            {
              configure.Protocols = HttpProtocols.Http2;
            });
          });

    var app = builder.Build();
    app.MapGrpcService<RaftGrpcService>();
    var serverTask = app.RunAsync();

    // Create client
    var channel = GrpcChannel.ForAddress(EndPoint.ToString());
    client = new RaftNode.RaftNodeClient(channel);
  }

  public override void ReplyToAppendEntries(int Term, bool Success)
  {
    throw new NotImplementedException();
  }

  public override void ReplyToRequestVote(int Term, bool VoteGranted)
  {
    throw new NotImplementedException();
  }

  public override (int, bool) SendAppendEntries(int RecieverId, int Term, int LeaderId, int PrevLogIndex, int PrevLogTerm, IEnumerable<RaftLogEntry<T>>? Entries, int LeaderCommit)
  {
    throw new NotImplementedException();
  }

  public override (int, bool) SendRequestVote(int RecieverId, int Term, int CandidateId, int LastLogIndex, int LastLogTerm)
  {
    throw new NotImplementedException();
  }
}
