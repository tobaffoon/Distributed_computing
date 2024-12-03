namespace RafRaft
{
   using Grpc.Core;
   using RafRaft.Domain;
   using RafRaft.Protos;

   using MapClient = Protos.RaftMapNode.RaftMapNodeClient;
   using Pair = KeyValuePair<string, Protos.Data>;
   using RaftNode = Domain.RaftNode<RaftMap.RaftMapStateMachine<Protos.Data>, KeyValuePair<string, Protos.Data>, Protos.Data>;

   public class RaftMapGrpcService : RaftMapNode.RaftMapNodeBase
   {
      private readonly RaftNode _node;

      public RaftMapGrpcService(IDictionary<int, MapClient> clients, int Id, long BroadcastTime, long ElectionTimeout)
      {
         RaftMapGrpcMediator mediator = new RaftMapGrpcMediator(clients);
         RaftNodeConfig config = new RaftNodeConfig(Id, BroadcastTime, ElectionTimeout, clients.Keys.ToList());
         _node = new RaftNode(config, mediator);
      }

      public override Task<AppendMapEntriesReply> Heartbeat(AppendMapEntriesRequest request, ServerCallContext context)
      {
         var reply = _node.HandleHeartbeatRequest(request.ConvertFromGrpc());
         return Task.FromResult(reply.ConvertToGrpc());
      }

      public override Task<AppendMapEntriesReply> AppendEntries(AppendMapEntriesRequest request, ServerCallContext context)
      {
         var reply = _node.HandleAppendEntriesRequest(request.ConvertFromGrpc());
         return Task.FromResult(reply.ConvertToGrpc());
      }

      public override Task<VoteMapReply> RequestVote(VoteMapRequest request, ServerCallContext context)
      {
         var reply = _node.HandleRequestVoteRequest(request.ConvertFromGrpc());
         return Task.FromResult(reply.ConvertToGrpc());
      }
   }
}
