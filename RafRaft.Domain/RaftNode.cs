using System.Threading;

namespace RafRaft.Domain;

public abstract class RaftNode<T>
{
  public enum State
  {
    Follower,
    Candidate,
    Leader
  }

  public readonly int Id;

  private State _nodeState = State.Follower;
  protected State NodeState
  {
    get => _nodeState;
    set
    {
      _nodeState = value;
    }
  }
  protected int currentTerm = 0;
  protected int? votedFor;
  protected abstract IEnumerable<RaftLogEntry<T>> Log
  {
    get;
  }
  protected int commitIndex = 0;
  protected int lastApplied = 0;
  protected abstract IEnumerable<int> NextIndex
  {
    get;
  }
  protected abstract IEnumerable<int> MatchIndex
  {
    get;
  }
  protected abstract T InternalState
  {
    get;
  }
  protected readonly long broadcastTimeout;
  protected readonly Timer broadcastTimer;
  protected readonly long electionTimeout;
  protected readonly Timer electionTimer;
  protected abstract IList<int> PeersIds
  {
    get;
  }
  protected int ClusterSize => PeersIds.Count + 1;
  protected int leaderId;
  protected int votesGot = 0;

  public RaftNode(long BroadcastTime, long ElectionTimeout)
  {
    broadcastTimeout = BroadcastTime;
    electionTimeout = ElectionTimeout;

    broadcastTimer = new Timer(OnBroadcastElapsed, null, Timeout.Infinite, broadcastTimeout);
    electionTimer = new Timer(OnElectionElapsed, null, Timeout.Infinite, electionTimeout);
  }

  public abstract (int, bool) SendAppendEntries(int RecieverId, int Term, int LeaderId, int PrevLogIndex,
    int PrevLogTerm, IEnumerable<RaftLogEntry<T>>? Entries, int LeaderCommit);

  public abstract (int, bool) SendRequestVote(int RecieverId, int Term, int CandidateId,
    int LastLogIndex, int LastLogTerm);

  public abstract void ReplyToAppendEntries(int Term, bool Success);

  public abstract void ReplyToRequestVote(int Term, bool VoteGranted);

  public abstract bool CompareEntries(RaftLogEntry<T> entryA, RaftLogEntry<T> entryB);

  /// <summary>
  /// 
  /// </summary>
  /// <param name="Entries"></param>
  /// <returns>Index of last new entry</returns>
  protected abstract int AppendEntries(IEnumerable<RaftLogEntry<T>>? Entries);

  public void StartUp()
  {
    broadcastTimer.Change(0, broadcastTimeout);
    electionTimer.Change(0, broadcastTimeout);
  }

  public void HandleAppendEntries(int Term, int LeaderId, int PrevLogIndex,
    int PrevLogTerm, IEnumerable<RaftLogEntry<T>>? Entries, int LeaderCommit)
  {
    if (Term < currentTerm)
    {
      ReplyToAppendEntries(currentTerm, false);
    }
    CorrectTerm(Term);
    CorrectLeader(LeaderId);

    RaftLogEntry<T>? prevEntry = Log.ElementAt(PrevLogIndex);
    if (prevEntry is null || prevEntry.Term != PrevLogTerm)
    {
      ReplyToAppendEntries(currentTerm, false);
    }

    int lastNewEntryId = AppendEntries(Entries);

    if (LeaderCommit > commitIndex)
    {
      commitIndex = Math.Min(LeaderCommit, lastNewEntryId);
    }

    ApplyCommited();
    ReplyToAppendEntries(currentTerm, true);
  }

  protected async void ApplyCommited()
  {
    for (; lastApplied < commitIndex; lastApplied++)
    {
      await Task.Run(() => Log.ElementAt(lastApplied + 1).action.Invoke(InternalState));
    }
  }

  protected void CorrectTerm(int Term)
  {
    if (Term > currentTerm)
    {
      currentTerm = Term;
      NodeState = State.Follower;
    }
  }

  protected void CorrectLeader(int LeaderId)
  {
    leaderId = LeaderId;
  }

  public void HandleRequestVote(int Term, int CandidateId,
    int LastLogIndex, int LastLogTerm)
  {
    if (Term < currentTerm)
    {
      ReplyToRequestVote(currentTerm, false);
    }
    CorrectTerm(Term);

    if (votedFor is null || votedFor == CandidateId)
    {
      votedFor = CandidateId;
      ReplyToRequestVote(currentTerm, true);
    }
  }

  protected void OnBroadcastElapsed(object? Ignored)
  {

  }
  protected void OnElectionElapsed(object? Ignored)
  {

  }
}