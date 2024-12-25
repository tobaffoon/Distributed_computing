namespace RafRaft.Domain
{
   using Messages;

   /// <summary>
   /// Coordinates sedning messages between raft nodes. Only controls the requests, not replies 
   /// (i.e. only clients, while replies are controled by a server, which launches separately).
   /// </summary>
   public interface IRaftMediator<TDataIn>
      where TDataIn : notnull
   {
      /// <summary>
      /// Sends AppendEntries to a node, awaits a reply.
      /// </summary>
      /// <param name="receiverId">Id of receiver node. Reference to node by id is up to implementing class</param>
      Task<AppendEntriesReply> SendAppendEntries(int receiverId, AppendEntriesRequest<TDataIn> request, CancellationToken token);

      /// <summary>
      /// Sends RequestVote to a node, awaits a reply.
      /// </summary>
      /// <param name="receiverId">Id of receiver node. Reference to node by id is up to implementing class</param>
      Task<VoteReply> SendRequestVote(int receiverId, VoteRequest request, CancellationToken token);
   }
}