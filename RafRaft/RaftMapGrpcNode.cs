namespace RafRaft
{
   using System.Net;
   using RafRaft.Protos;
   using Microsoft.AspNetCore.Server.Kestrel.Core;
   using Grpc.Net.Client;
   using RafRaft.Services;
   using RafRaft.RaftMap;

   using Client = Protos.RaftMediator.RaftMediatorClient;
   using LogEntry = Domain.RaftLogEntry<KeyValuePair<string, Protos.Data>>;

   public class RaftMapGrpcNode : RaftMapNode<Data>
   {
      private readonly Dictionary<int, Client> clients;

      public RaftMapGrpcNode(IPEndPoint EndPoint, int Id, long BroadcastTime, long ElectionTimeout, List<RaftGrpcNodeConfig> PeersConfigs)
        : base(Id, BroadcastTime, ElectionTimeout, PeersConfigs.Select(node => node.Id))
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
            clients[nodeConfig.Id] = new Client(channel);
         }
      }

      public override Task RedirectUserRequest(int LeaderId, object Data)
      {
         throw new NotImplementedException();
      }

      public override Task ReplyToAppendEntries(int SenderId, int Term, bool Success)
      {
         throw new NotImplementedException();
      }

      public override Task ReplyToRequestVote(int SenderId, int Term, bool VoteGranted)
      {
         throw new NotImplementedException();
      }

      public override async Task<(int, bool)> SendAppendEntries(int RecieverId, int Term, int LeaderId, int PrevLogIndex, int PrevLogTerm, IEnumerable<LogEntry>? Entries, int LeaderCommit)
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
            foreach (var entry in Entries)
            {
               request.Entries.Add(entry.Data.Key, entry.Data.Value);
            }
         }

         AppendEntriesReply reply = await reciever.AppendEntriesAsync(request);
         return (reply.Term, reply.Success);
      }

      public override async Task<(int, bool)> SendRequestVote(int RecieverId, int Term, int CandidateId, int LastLogIndex, int LastLogTerm)
      {
         var reciever = clients[RecieverId];
         var request = new VoteRequest
         {
            Term = Term,
            CandidateId = CandidateId,
            LastLogId = LastLogIndex,
            LastLogTerm = LastLogTerm
         };

         VoteReply reply = await reciever.RequestVoteAsync(request);
         return (reply.Term, reply.VoteGranted);
      }

      /// <summary>
      /// Used to determine the broadcast time.
      /// The method must implement a dummy request to the state machine, in order to correctly approximate the broadcast time.
      /// </summary>
      void TestConnection()
      {
         // TODO
      }
   }
}