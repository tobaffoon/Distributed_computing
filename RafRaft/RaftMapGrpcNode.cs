namespace RafRaft
{
   using RafRaft.Domain;
   using Protos;

   public class RaftMapGrpcNode : RaftNode<RaftMap.RaftMapStateMachine<Protos.Data>, KeyValuePair<string, Protos.Data>, Protos.Data>
   {
      public RaftMapGrpcNode(RaftNodeConfig config, RaftMapGrpcMediator mediator, ILogger logger, bool isInitNode) : base(config, mediator, logger, isInitNode)
      {
      }

      public async Task<SetReply> HandleUserSetRequest(SetRequest setRequest)
      {
         if (nodeState != State.Leader)
         {
            string redirectMessage;
            if (leaderId == null)
            {
               redirectMessage = "Node is not a leader, leader is unknown";
            }
            else
            {
               redirectMessage = $"Node is not a leader, leader is {((RaftMapGrpcMediator)_mediator).Names[leaderId.Value]}";
            }
            return new SetReply()
            {
               Message = redirectMessage
            };
         }

         int newEntryIndex = log.Count;
         var newEntry = new RaftLogEntry<KeyValuePair<string, Data>>(
            newEntryIndex,
            currentTerm,
            new KeyValuePair<string, Data>(setRequest.Key, setRequest.Value));
         log.Add(newEntry);

         async Task WaitApply()
         {
            while (lastApplied < newEntryIndex)
            {
               _logger.LogTrace("lastApplies: {l}; newEntryIndex: {n}", lastApplied, newEntryIndex);
               await Task.Run(() => Thread.Sleep(1000));
            }
         }

         _logger.LogTrace("Start applying");
         await WaitApply();
         _logger.LogTrace("Finish applying");
         return new SetReply()
         {
            Message = "Success"
         };
      }

      public GetReply HandleUserGetRequest(GetRequest getRequest)
      {
         GetReply reply = new GetReply
         {
            Key = getRequest.Key,
            Value = internalState.RequestData(getRequest.Key)
         };
         _logger.LogTrace("Reply: {reply}", reply);
         return reply;
      }

      public class RaftMapNodeBase
      {
      }
   }
}