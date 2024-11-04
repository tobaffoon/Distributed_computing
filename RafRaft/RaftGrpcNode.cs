using System;
using RafRaft.Domain;

namespace RafRaft;

public class RaftGrpcNode<T> : RaftNode<T> where T : new()
{
  public RaftGrpcNode(long BroadcastTime, long ElectionTimeout) : base(BroadcastTime, ElectionTimeout)
  {
  }

  protected override IEnumerable<RaftLogEntry<T>> Log => throw new NotImplementedException();

  protected override IEnumerable<int> NextIndex => throw new NotImplementedException();

  protected override IEnumerable<int> MatchIndex => throw new NotImplementedException();

  protected override IList<int> NodeIds => throw new NotImplementedException();

  public override bool CompareEntries(RaftLogEntry<T> entryA, RaftLogEntry<T> entryB)
  {
    throw new NotImplementedException();
  }

  public override void ReplyToAppendEntries(int Term, bool Success)
  {
    throw new NotImplementedException();
  }

  public override void ReplyToRequestVote(int Term, bool VoteGranted)
  {
    throw new NotImplementedException();
  }

  public override (int, bool) SendAppendEntries(int RecieverId, int Term, int LeaderId, int PrevLogIndex, int PrevLogTerm, IEnumerable<RaftLogEntry<T>>? Entries, int LeaderCommit)
  {
    throw new NotImplementedException();
  }

  public override (int, bool) SendRequestVote(int RecieverId, int Term, int CandidateId, int LastLogIndex, int LastLogTerm)
  {
    throw new NotImplementedException();
  }

  protected override int AppendEntries(IEnumerable<RaftLogEntry<T>>? Entries)
  {
    throw new NotImplementedException();
  }
}
