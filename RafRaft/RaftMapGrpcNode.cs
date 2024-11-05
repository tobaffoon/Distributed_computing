using System.Net;
using RafRaft.Protos;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Grpc.Net.Client;
using RaftRaft.Services;
using RafRaft.Domain;
using Google.Protobuf;

namespace RafRaft;

public class RaftMapGrpcNode : RaftNode<RaftMapEntry<string, int>, Dictionary<string, int>>
{
  private readonly Dictionary<int, RaftNode.RaftNodeClient> clients;

  public RaftMapGrpcNode(IPEndPoint EndPoint, int Id, long BroadcastTime, long ElectionTimeout, List<RaftGrpcNodeConfig> PeersConfigs)
    : base(Id, BroadcastTime, ElectionTimeout,
            from node in PeersConfigs select node.Id)
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

    // Create clients
    clients = [];
    foreach (RaftGrpcNodeConfig nodeConfig in PeersConfigs)
    {
      var channel = GrpcChannel.ForAddress(nodeConfig.EndPoint.ToString());
      clients[nodeConfig.Id] = new RaftNode.RaftNodeClient(channel);
    }
  }

  public override Task ReplyToAppendEntries(int Term, bool Success)
  {
    throw new NotImplementedException();
  }

  public override Task ReplyToRequestVote(int Term, bool VoteGranted)
  {
    throw new NotImplementedException();
  }

  public override async Task<(int, bool)> SendAppendEntries(int RecieverId, int Term, int LeaderId, int PrevLogIndex, int PrevLogTerm, IEnumerable<RaftMapEntry<string, int>>? Entries, int LeaderCommit)
  {
    var reciever = clients[RecieverId];
    var request = new AppendEntriesRequest
    {
      Term = Term,
      LeaderId = LeaderId,
      PrevLogId = PrevLogIndex,
      PrevLogTerm = PrevLogTerm,
      LeaderCommitId = LeaderCommit
    };
    if (Entries is not null)
    {
      IDictionary<string, int> dict = Entries.ToDictionary(entry => entry.KvData.Item1, entry => entry.KvData.Item2);
      request.Entries.Add(dict);
    }

    var reply = await reciever.AppendEntriesAsync(request);
    return (reply.Term, reply.Success);
  }

  public override Task<(int, bool)> SendRequestVote(int RecieverId, int Term, int CandidateId, int LastLogIndex, int LastLogTerm)
  {
    throw new NotImplementedException();
  }
}
