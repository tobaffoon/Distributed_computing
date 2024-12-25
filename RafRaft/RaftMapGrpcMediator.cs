namespace RafRaft
{
   using RafRaft.Protos;
   using System.Threading.Tasks;
   using RafRaft.Domain.Messages;

   using MapClient = Protos.RaftMapNode.RaftMapNodeClient;
   using RafRaft.Domain;
   using Grpc.Net.Client;
   using Grpc.Core;
   using Grpc.Net.ClientFactory;

   public class RaftMapGrpcMediator : RaftGrpcMediator<KeyValuePair<string, Data>, MapClient>
   {
      private readonly IReadOnlyDictionary<int, MapClient> _clients;
      public IReadOnlyDictionary<int, MapClient> Clients { get => _clients; }

      private readonly int _id;
      private readonly ILogger _logger;
      private readonly Dictionary<int, GrpcChannel> _channels;

      public RaftMapGrpcMediator(IDictionary<int, MapClient> clients, IDictionary<int, GrpcChannel> channels, RaftNodeConfig nodeConfig, ILogger logger)
      {
         _clients = new Dictionary<int, MapClient>(clients);
         _channels = new Dictionary<int, GrpcChannel>(channels);
         _id = nodeConfig.Id;
         _logger = logger;
      }

      public async Task<AppendEntriesReply> SendAppendEntries(
         int receiverId,
         AppendEntriesRequest<KeyValuePair<string, Data>> request)
      {
         // _logger.LogInformation("Send Append Entries to #{id} with status {status}", receiverId, _channels[receiverId].State);
         AppendMapEntriesRequest grpcRequest = request.ConvertToGrpc();
         AppendMapEntriesReply grpcReply = await Clients[receiverId].AppendEntriesAsync(grpcRequest);
         AppendEntriesReply reply = grpcReply.ConvertFromGrpc();
         return reply;
      }

      public async Task<VoteReply> SendRequestVote(
         int receiverId,
         VoteRequest request)
      {
         // _logger.LogInformation("Send RequestVote to #{id} with status {status}", receiverId, _channels[receiverId].State);
         VoteMapRequest grpcRequest = request.ConvertToGrpc();
         VoteMapReply grpcReply = await Clients[receiverId].RequestVoteAsync(grpcRequest);
         VoteReply reply = grpcReply.ConvertFromGrpc();
         return reply;
      }
   }
}