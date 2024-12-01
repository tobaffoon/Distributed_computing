namespace RafRaft.RaftMap
{
   using RafRaft.Domain;

   public abstract class RaftMapNode<T> : RaftNode<RaftMapStateMachine<T>, KeyValuePair<string, T>, T>
      where T : notnull
   {
      protected RaftMapNode(int Id, long BroadcastTime, long ElectionTimeout, IEnumerable<int> NodeIds) : base(Id, BroadcastTime, ElectionTimeout, NodeIds)
      {
      }
   }
}