namespace RafRaft
{
   using Grpc.Core;
   using RafRaft.Domain;
   using RafRaft.Protos;

   using RaftNode = Domain.RaftNode<RaftMap.RaftMapStateMachine<Protos.Data>, KeyValuePair<string, Protos.Data>, Protos.Data>;
   using Empty = Google.Protobuf.WellKnownTypes.Empty;

   public class RaftMapGrpcServer : RaftMapNode.RaftMapNodeBase
   {
      private readonly RaftMapGrpcNode _node;
      private readonly ILogger _logger;
      private bool _started = false;

      public RaftMapGrpcServer(RaftMapGrpcMediator mediator, RaftNodeConfig config, ILogger logger, bool isInitNode)
      {
         _logger = logger;
         _node = new RaftMapGrpcNode(config, mediator, _logger, isInitNode);
      }

      public void Start(IDictionary<int, bool> peersStatus)
      {
         _started = true;
         _node.StartUp(peersStatus);
      }

      public override async Task<AppendMapEntriesReply> AppendEntries(AppendMapEntriesRequest request, ServerCallContext context)
      {
         if (!_started)
         {
            throw new RpcException(new Status(StatusCode.Unavailable, "Server has not started yet"));
         }

         var reply = _node.HandleAppendEntriesRequest(request.ConvertFromGrpc());
         return reply.ConvertToGrpc();
      }

      public override Task<VoteMapReply> RequestVote(VoteMapRequest request, ServerCallContext context)
      {
         if (!_started)
         {
            throw new RpcException(new Status(StatusCode.Unavailable, "Server has not started yet"));
         }

         var reply = _node.HandleRequestVoteRequest(request.ConvertFromGrpc());
         return Task.FromResult(reply.ConvertToGrpc());
      }

      public override Task<Empty> TestConnection(Empty e, ServerCallContext context)
      {
         _logger.LogInformation("Received testConnection request from {peer}", context.Peer);
         return Task.FromResult(e);
      }

      public override Task<SetReply> Set(SetRequest setRequest, ServerCallContext context)
      {
         _logger.LogInformation("Received Set request: {request}, {data}", setRequest, setRequest.Value.DataCase);
         return _node.HandleUserSetRequest(setRequest);
      }

      public override Task<GetReply> Get(GetRequest getRequest, ServerCallContext context)
      {
         _logger.LogInformation("Received Get request: {request}", getRequest);
         return Task.FromResult(_node.HandleUserGetRequest(getRequest));
      }
   }
}
