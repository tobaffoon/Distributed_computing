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

  protected State nodeState = State.Follower;
  protected int currentTerm = 0;
  protected int? votedFor;
  protected abstract IEnumerable<RaftLogEntry<T>> Log
  {
    get;
  }
  protected int commitIndex = 0;
  protected int commitTerm = 0;
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
  protected abstract IList<int> NodeIds
  {
    get;
  }
  protected int ClusterSize => NodeIds.Count + 1;
  protected int leaderId;
  protected int votesGot = 0;
  private int _heartbeatRecievedBackValue = 0;
  protected bool HeartbeatRecieved
  {
    get { return Interlocked.CompareExchange(ref _heartbeatRecievedBackValue, 1, 1) == 1; }
    set
    {
      if (value) Interlocked.CompareExchange(ref _heartbeatRecievedBackValue, 1, 0);
      else Interlocked.CompareExchange(ref _heartbeatRecievedBackValue, 0, 1);
    }
  }

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
  /// Must set commitIndex and commitTerm.
  /// </summary>
  /// <param name="Entries"></param>
  /// <returns>Index of last new entry</returns>
  protected abstract int AppendEntries(IEnumerable<RaftLogEntry<T>>? Entries);

  public void StartUp()
  {
    electionTimer.Change(0, broadcastTimeout);
  }

  public void HandleAppendEntries(int Term, int LeaderId, int PrevLogIndex,
    int PrevLogTerm, IEnumerable<RaftLogEntry<T>>? Entries, int LeaderCommit)
  {
    HeartbeatRecieved = true;

    #region Term correction
    if (Term < currentTerm)
    {
      ReplyToAppendEntries(currentTerm, false);
    }
    CorrectTerm(Term);
    #endregion

    CorrectLeader(LeaderId);

    #region Previous log entry discovery
    RaftLogEntry<T>? prevEntry = Log.ElementAt(PrevLogIndex);
    if (prevEntry is null || prevEntry.Term != PrevLogTerm)
    {
      ReplyToAppendEntries(currentTerm, false);
    }
    #endregion

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
      BecomeFollower();
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

    if (CanGrantVote(CandidateId) && IsNewLogBetter(LastLogIndex, LastLogTerm))
    {
      votedFor = CandidateId;
      ReplyToRequestVote(currentTerm, true);
    }
    else
    {
      ReplyToRequestVote(currentTerm, false);
    }
  }
  private bool CanGrantVote(int CandidateId)
  {
    return votedFor is null || votedFor == CandidateId;
  }
  private bool IsNewLogBetter(int NewLogIndex, int NewLogTerm)
  {
    return commitTerm <= NewLogTerm && commitIndex <= NewLogIndex;
  }

  protected void OnBroadcastElapsed(object? Ignored)
  {
    // Heartbeat
    foreach (int nodeId in NodeIds)
    {
      // TODO check return value for degrading to follower i guess
      SendAppendEntries(nodeId, currentTerm, Id, commitIndex, commitTerm, null, commitIndex);
    }
  }

  protected async void BeginElection()
  {
    votesGot = 0;
    currentTerm++;

    votedFor = Id;
    votesGot++;

    List<Task> voteList = new List<Task>();
    foreach (int nodeId in NodeIds)
    {
      // TODO add cancelettion token wor downgrading case
      var voteTask = new Task<(int, bool)>(() => SendRequestVote(nodeId, currentTerm, Id, commitIndex, commitTerm));
      var replyTask = voteTask.ContinueWith((task) => HandleRequestVoteReply(task.Result.Item1, task.Result.Item2));
      voteList.Add(voteTask);
      voteList.Add(replyTask);
    }
    await Task.WhenAll(voteList);

    if (VotesAreEnough())
    {
      BecomeLeader();
    }
  }
  protected void HandleRequestVoteReply(int Term, bool VoteGranted)
  {
    CorrectTerm(Term);
    if (nodeState == State.Candidate && VoteGranted) votesGot++;
  }
  protected void OnElectionElapsed(object? Ignored)
  {
    if (nodeState != State.Leader) return;

    if (!HeartbeatRecieved)
    {
      BecomeCandidate();
      return;
    }

    HeartbeatRecieved = false;
  }

  protected void BecomeLeader()
  {
    broadcastTimer.Change(0, broadcastTimeout);
    //TODO: init next index
    //TODO: init match index
  }

  protected void BecomeCandidate()
  {
    DemoteFromLeadership();
    nodeState = State.Leader;
    BeginElection();
  }

  protected void BecomeFollower()
  {
    DemoteFromLeadership();
    nodeState = State.Follower;
  }

  protected void DemoteFromLeadership()
  {
    broadcastTimer.Change(Timeout.Infinite, broadcastTimeout); // or both Infinte?
  }

  protected bool VotesAreEnough()
  {
    return votesGot * 2 > ClusterSize;
  }
}