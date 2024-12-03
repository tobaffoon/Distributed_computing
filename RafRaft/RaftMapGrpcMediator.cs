namespace RafRaft
{
   using RafRaft.Protos;
   using System.Collections.ObjectModel;
   using System.Threading.Tasks;
   using RafRaft.Domain.Messages;

   using MapClient = Protos.RaftMapNode.RaftMapNodeClient;
   using LogEntry = Domain.RaftLogEntry<KeyValuePair<string, Protos.Data>>;

   public class RaftMapGrpcMediator : RaftGrpcMediator<KeyValuePair<string, Data>, MapClient>
   {
      private ReadOnlyDictionary<int, MapClient> _clients;
      public ReadOnlyDictionary<int, MapClient> Clients { get => _clients; }

      public RaftMapGrpcMediator(IDictionary<int, MapClient> clients)
      {
         _clients = new ReadOnlyDictionary<int, MapClient>(clients);
      }

      public async Task<AppendEntriesReply> SendAppendEntries(int receiverId, AppendEntriesRequest<KeyValuePair<string, Data>> request)
      {
         AppendMapEntriesRequest grpcRequest = request.ConvertToGrpc();
         AppendMapEntriesReply grpcReply = await Clients[receiverId].AppendEntriesAsync(grpcRequest);
         AppendEntriesReply reply = grpcReply.ConvertFromGrpc();
         return reply;
      }

      public async Task<AppendEntriesReply> SendHeartbeat(int receiverId, AppendEntriesRequest<KeyValuePair<string, Data>> request)
      {
         AppendMapEntriesRequest grpcRequest = request.ConvertToGrpc();
         AppendMapEntriesReply grpcReply = await Clients[receiverId].HeartbeatAsync(grpcRequest);
         AppendEntriesReply reply = grpcReply.ConvertFromGrpc();
         return reply;
      }

      public async Task<VoteReply> SendRequestVote(int receiverId, VoteRequest request)
      {
         VoteMapRequest grpcRequest = request.ConvertToGrpc();
         VoteMapReply grpcReply = await Clients[receiverId].RequestVoteAsync(grpcRequest);
         VoteReply reply = grpcReply.ConvertFromGrpc();
         return reply;
      }


   }
}