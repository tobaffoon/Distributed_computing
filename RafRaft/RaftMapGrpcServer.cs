namespace RafRaft
{
   using Grpc.Core;
   using RafRaft.Domain;
   using RafRaft.Protos;

   using MapClient = Protos.RaftMapNode.RaftMapNodeClient;
   using Pair = KeyValuePair<string, Protos.Data>;
   using RaftNode = Domain.RaftNode<RaftMap.RaftMapStateMachine<Protos.Data>, KeyValuePair<string, Protos.Data>, Protos.Data>;

   public class RaftMapGrpcServer : RaftMapNode.RaftMapNodeBase
   {
      private readonly RaftNode _node;
      private readonly ILogger<RaftMapGrpcServer> _logger;

      public RaftMapGrpcServer(RaftMapGrpcMediator mediator, RaftNodeConfig config, ILogger<RaftMapGrpcServer> logger)
      {
         _logger = logger;
         _node = new RaftNode(config, mediator);
         _node.StartUp();

         _logger.LogInformation("Start {id} node", config.Id);
      }

      public override Task<AppendMapEntriesReply> Heartbeat(AppendMapEntriesRequest request, ServerCallContext context)
      {
         _logger.LogInformation("Received Heartbeat request from {id}", request.LeaderId);
         var reply = _node.HandleHeartbeatRequest(request.ConvertFromGrpc());
         return Task.FromResult(reply.ConvertToGrpc());
      }

      public override Task<AppendMapEntriesReply> AppendEntries(AppendMapEntriesRequest request, ServerCallContext context)
      {
         _logger.LogInformation("Received AppendEntries request from {id}", request.LeaderId);
         var reply = _node.HandleAppendEntriesRequest(request.ConvertFromGrpc());
         return Task.FromResult(reply.ConvertToGrpc());
      }

      public override Task<VoteMapReply> RequestVote(VoteMapRequest request, ServerCallContext context)
      {
         _logger.LogInformation("Received RequestVote request from {id}", request.CandidateId);
         var reply = _node.HandleRequestVoteRequest(request.ConvertFromGrpc());
         return Task.FromResult(reply.ConvertToGrpc());
      }
   }
}
