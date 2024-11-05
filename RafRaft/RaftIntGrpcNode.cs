using System;
using System.Net;
using RafRaft.Domain;

namespace RafRaft;

public class RaftIntGrpcNode : RaftGrpcNode<int>
{
  public RaftIntGrpcNode(IPEndPoint EndPoint, int Id, long BroadcastTime, long ElectionTimeout, IEnumerable<int> NodeIds) : base(EndPoint, Id, BroadcastTime, ElectionTimeout, NodeIds)
  {
  }

  public override bool CompareEntries(RaftLogEntry<int> entryA, RaftLogEntry<int> entryB)
  {
    throw new NotImplementedException();
  }
}
