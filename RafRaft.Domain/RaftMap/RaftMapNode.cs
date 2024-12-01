
namespace RafRaft.Domain;

public abstract class RaftMapNode : RaftNode<RaftMapStateMachine, Dictionary<string, object>, object>
{
   protected RaftMapNode(int Id, long BroadcastTime, long ElectionTimeout, IEnumerable<int> NodeIds) : base(Id, BroadcastTime, ElectionTimeout, NodeIds)
   {
   }
}
