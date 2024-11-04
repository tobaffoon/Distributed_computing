using System;

namespace RafRaft.Domain;

public abstract class RaftListNode<T> : RaftNode<T>
{
  protected RaftListNode(long BroadcastTime, long ElectionTimeout) : base(BroadcastTime, ElectionTimeout)
  {
  }

  private List<RaftLogEntry<T>> _log = new List<RaftLogEntry<T>>();
  protected override IEnumerable<RaftLogEntry<T>> Log => _log;

  private List<int> _nextIndex = new List<int>();
  protected override IEnumerable<int> NextIndex => _nextIndex;

  private List<int> _matchIndex = new List<int>();
  protected override IEnumerable<int> MatchIndex => _matchIndex;

  protected override int AppendEntries(IEnumerable<RaftLogEntry<T>>? Entries)
  {
    if (Entries is null) return commitIndex; // or appened idk

    foreach (RaftLogEntry<T> entry in Entries)
    {
      _log.Add(entry);
      commitIndex = entry.Index;
      commitTerm = entry.Term;
    }

    return commitIndex;
  }
}
