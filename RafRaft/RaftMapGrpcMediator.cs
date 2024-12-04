namespace RafRaft
{
   using RafRaft.Protos;
   using System.Threading.Tasks;
   using RafRaft.Domain.Messages;

   using MapClient = Protos.RaftMapNode.RaftMapNodeClient;
   using RafRaft.Domain;

   public class RaftMapGrpcMediator : RaftGrpcMediator<KeyValuePair<string, Data>, MapClient>
   {
      private readonly IReadOnlyDictionary<int, MapClient> _clients;
      public IReadOnlyDictionary<int, MapClient> Clients { get => _clients; }

      private readonly int _id;
      private readonly ILogger<RaftMapGrpcMediator> _logger;

      public RaftMapGrpcMediator(IDictionary<int, MapClient> clients, RaftNodeConfig nodeConfig, ILogger<RaftMapGrpcMediator> logger)
      {
         _clients = new Dictionary<int, MapClient>(clients);
         _id = nodeConfig.Id;
         _logger = logger;
      }

      public async Task<AppendEntriesReply> SendAppendEntries(
         int receiverId,
         AppendEntriesRequest<KeyValuePair<string, Data>> request,
         CancellationToken token)
      {
         _logger.LogInformation("Send AppendEntries request to Node #{id}", receiverId);

         AppendMapEntriesRequest grpcRequest = request.ConvertToGrpc();
         AppendMapEntriesReply grpcReply = await Clients[receiverId].AppendEntriesAsync(grpcRequest, cancellationToken: token);
         AppendEntriesReply reply = grpcReply.ConvertFromGrpc();
         return reply;
      }

      public async Task<AppendEntriesReply> SendHeartbeat(
         int receiverId,
         AppendEntriesRequest<KeyValuePair<string, Data>> request,
         CancellationToken token)
      {
         _logger.LogInformation("Send Heartbeat request to Node #{id}", receiverId);

         AppendMapEntriesRequest grpcRequest = request.ConvertToGrpc();
         AppendMapEntriesReply grpcReply = await Clients[receiverId].HeartbeatAsync(grpcRequest, cancellationToken: token);
         AppendEntriesReply reply = grpcReply.ConvertFromGrpc();
         return reply;
      }

      public async Task<VoteReply> SendRequestVote(
         int receiverId,
         VoteRequest request,
         CancellationToken token)
      {
         _logger.LogInformation("Send RequestVote request to Node #{id} for Term {term}", receiverId, request.Term);

         VoteMapRequest grpcRequest = request.ConvertToGrpc();
         VoteMapReply grpcReply = await Clients[receiverId].RequestVoteAsync(grpcRequest, cancellationToken: token);
         VoteReply reply = grpcReply.ConvertFromGrpc();
         return reply;
      }
   }
}