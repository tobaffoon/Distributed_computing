namespace RafRaft.Domain;

public abstract class RaftListNode<T> : RaftNode<T> where T : new()
{
  protected RaftListNode(long BroadcastTime, long ElectionTimeout, IEnumerable<int> NodeIds) : base(BroadcastTime, ElectionTimeout, NodeIds)
  {
  }

  private readonly List<RaftLogEntry<T>> _log = [];
  protected override IEnumerable<RaftLogEntry<T>> Log => _log;

  private readonly List<int> _nextIndex = [];
  protected override IEnumerable<int> NextIndex => _nextIndex;

  private readonly List<int> _matchIndex = [];
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
