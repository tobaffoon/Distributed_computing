namespace RafRaft.Services
{
   using Grpc.Core;
   using RafRaft.Domain;
   using RafRaft.Protos;

   public class RaftGrpcService : RaftMediator.RaftMediatorBase
   {
      public override Task<AppendEntriesReply> Heartbeat(AppendEntriesRequest request, ServerCallContext context)
      {
         return Task.FromResult(new AppendEntriesReply
         {
            Term = 1,
            Success = true
         });
      }

      public override Task<AppendEntriesReply> AppendEntries(AppendEntriesRequest request, ServerCallContext context)
      {
         return Task.FromResult(new AppendEntriesReply
         {
            Term = 1,
            Success = true
         });
      }

      public override Task<VoteReply> RequestVote(VoteRequest request, ServerCallContext context)
      {
         return Task.FromResult(new VoteReply
         {
            Term = 1,
            VoteGranted = false
         });
      }
   }
}
