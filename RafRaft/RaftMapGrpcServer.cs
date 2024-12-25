namespace RafRaft
{
   using Grpc.Core;
   using RafRaft.Domain;
   using RafRaft.Protos;

   using RaftNode = Domain.RaftNode<RaftMap.RaftMapStateMachine<Protos.Data>, KeyValuePair<string, Protos.Data>, Protos.Data>;
   using Empty = Google.Protobuf.WellKnownTypes.Empty;

   public class RaftMapGrpcServer : RaftMapNode.RaftMapNodeBase
   {
      private readonly RaftNode _node;
      private readonly ILogger _logger;
      private bool _started = false;

      public RaftMapGrpcServer(RaftMapGrpcMediator mediator, RaftNodeConfig config, ILogger logger)
      {
         _logger = logger;
         _node = new RaftNode(config, mediator, _logger);
      }

      public void Start(IDictionary<int, bool> peersStatus)
      {
         _started = true;
         _node.StartUp(peersStatus);
      }

      public override Task<AppendMapEntriesReply> Heartbeat(AppendMapEntriesRequest request, ServerCallContext context)
      {
         if (!_started)
         {
            throw new RpcException(new Status(StatusCode.Unavailable, "Server has not started yet"));
         }

         if (context.CancellationToken.IsCancellationRequested)
         {
            throw new RpcException(new Status(StatusCode.Cancelled, ""));
         }
         var reply = _node.HandleHeartbeatRequest(request.ConvertFromGrpc());
         return Task.FromResult(reply.ConvertToGrpc());
      }

      public override Task<AppendMapEntriesReply> AppendEntries(AppendMapEntriesRequest request, ServerCallContext context)
      {
         if (!_started)
         {
            throw new RpcException(new Status(StatusCode.Unavailable, "Server has not started yet"));
         }

         if (context.CancellationToken.IsCancellationRequested)
         {
            throw new RpcException(new Status(StatusCode.Cancelled, ""));
         }
         var reply = _node.HandleAppendEntriesRequest(request.ConvertFromGrpc());
         return Task.FromResult(reply.ConvertToGrpc());
      }

      public override Task<VoteMapReply> RequestVote(VoteMapRequest request, ServerCallContext context)
      {
         if (!_started)
         {
            throw new RpcException(new Status(StatusCode.Unavailable, "Server has not started yet"));
         }

         if (context.CancellationToken.IsCancellationRequested)
         {
            throw new RpcException(new Status(StatusCode.Cancelled, ""));
         }
         var reply = _node.HandleRequestVoteRequest(request.ConvertFromGrpc());
         return Task.FromResult(reply.ConvertToGrpc());
      }

      public override Task<Empty> TestConnection(Empty e, ServerCallContext context)
      {
         _logger.LogInformation("Received testConnection request from {peer}", context.Peer);
         return Task.FromResult(e);
      }
   }
}
