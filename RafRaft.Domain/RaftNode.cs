using System.Threading;

namespace RafRaft.Domain;

public abstract class RaftNode<Entry, T>
  where Entry : RaftLogEntry<T>
  where T : new()
{
  public enum State
  {
    Follower,
    Candidate,
    Leader
  }

  public readonly int id;

  protected State nodeState = State.Follower;
  private int _currentTerm = 0;
  protected int CurrentTerm
  {
    get => _currentTerm;
    set => _currentTerm = value;
  }
  protected int? votedFor;
  protected List<Entry> log;
  protected int commitIndex = 0;
  protected int commitTerm = 0;
  protected int lastApplied = 0;
  //TODO Implement NextIndex evaluation
  protected List<int> nextIndex;
  //TODO Implement MatchIndex evaluation
  protected List<int> matchIndex;
  protected readonly T internalState;
  protected readonly long broadcastTimeout;
  protected readonly Timer broadcastTimer;
  protected readonly long electionTimeout;
  protected readonly Timer electionTimer;
  protected List<int> nodeIds;
  protected int ClusterSize => nodeIds.Count + 1;
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

  public RaftNode(int Id, long BroadcastTime, long ElectionTimeout, IEnumerable<int> NodeIds)
  {
    broadcastTimeout = BroadcastTime;
    electionTimeout = ElectionTimeout;

    broadcastTimer = new Timer(OnBroadcastElapsed, null, Timeout.Infinite, broadcastTimeout);
    electionTimer = new Timer(OnElectionElapsed, null, Timeout.Infinite, electionTimeout);

    internalState = new T();

    id = Id;

    var nodesWithoutThis = NodeIds.Except([id]);
    nodeIds = new List<int>(nodesWithoutThis);

    nextIndex = new List<int>(nodeIds.Count);
    matchIndex = new List<int>(nodeIds.Count);

    log = new List<Entry>();
  }

  public abstract Task<(int, bool)> SendAppendEntries(int RecieverId, int Term, int LeaderId, int PrevLogIndex,
    int PrevLogTerm, IEnumerable<Entry>? Entries, int LeaderCommit);

  public abstract Task<(int, bool)> SendRequestVote(int RecieverId, int Term, int CandidateId,
    int LastLogIndex, int LastLogTerm);

  public abstract Task ReplyToAppendEntries(int SenderId, int Term, bool Success);

  public abstract Task ReplyToRequestVote(int SenderId, int Term, bool VoteGranted);

  public abstract Task RedirectUserRequest(int LeaderId, object Data);

  protected int AppendEntries(IEnumerable<Entry>? Entries)
  {
    if (Entries is null) return commitIndex; // or appened idk

    foreach (Entry entry in Entries)
    {
      log.Add(entry);
      commitIndex = entry.Index;
      commitTerm = entry.Term;
    }

    return commitIndex;
  }

  public void StartUp()
  {
    electionTimer.Change(0, broadcastTimeout);
  }

  public void HandleAppendEntries(int Term, int LeaderId, int PrevLogIndex,
    int PrevLogTerm, IEnumerable<Entry>? Entries, int LeaderCommit)
  {
    HeartbeatRecieved = true;

    #region Term correction
    if (Term < CurrentTerm)
    {
      ReplyToAppendEntries(LeaderId, CurrentTerm, false);
    }
    CorrectTerm(Term);
    #endregion

    CorrectLeader(LeaderId);

    #region Previous log entry discovery
    Entry? prevEntry = log[PrevLogIndex];
    if (prevEntry is null || prevEntry.Term != PrevLogTerm)
    {
      ReplyToAppendEntries(LeaderId, CurrentTerm, false);
    }
    #endregion

    int lastNewEntryId = AppendEntries(Entries);

    if (LeaderCommit > commitIndex)
    {
      commitIndex = Math.Min(LeaderCommit, lastNewEntryId);
    }

    ApplyCommited();

    ReplyToAppendEntries(LeaderId, CurrentTerm, true);
  }

  protected async void ApplyCommited()
  {
    for (; lastApplied < commitIndex; lastApplied++)
    {
      await Task.Run(() => log[lastApplied + 1].Apply(internalState));
    }
  }

  protected void CorrectTerm(int Term)
  {
    if (Term > CurrentTerm)
    {
      CurrentTerm = Term;
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
    if (Term < CurrentTerm)
    {
      ReplyToRequestVote(CandidateId, CurrentTerm, false);
    }
    CorrectTerm(Term);

    if (CanGrantVote(CandidateId) && IsNewLogBetter(LastLogIndex, LastLogTerm))
    {
      votedFor = CandidateId;
      ReplyToRequestVote(CandidateId, CurrentTerm, true);
    }
    else
    {
      ReplyToRequestVote(CandidateId, CurrentTerm, false);
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

  protected async void OnBroadcastElapsed(object? Ignored)
  {
    // Heartbeat
    List<Task> taskList = new List<Task>();
    foreach (int nodeId in nodeIds)
    {
      // TODO add cancelettion token for downgrading to follower case
      var requestTask = SendAppendEntries(nodeId, CurrentTerm, id, commitIndex, commitTerm, null, commitIndex);
      var replyTask = requestTask.ContinueWith((task) => HandleHeartbeatReply(task.Result.Item1, task.Result.Item2));
      taskList.Add(requestTask);
      taskList.Add(replyTask);
    }

    await Task.WhenAll(taskList);
  }

  protected void HandleHeartbeatReply(int Term, bool Success)
  {
    CorrectTerm(Term);
  }

  protected async void BeginElection()
  {
    votesGot = 0;
    CurrentTerm++;

    votedFor = id;
    votesGot++;

    List<Task> taskList = new List<Task>();
    foreach (int nodeId in nodeIds)
    {
      // TODO add cancelettion token for downgrading case
      var voteTask = SendRequestVote(nodeId, CurrentTerm, id, commitIndex, commitTerm);
      var replyTask = voteTask.ContinueWith((task) => HandleRequestVoteReply(task.Result.Item1, task.Result.Item2));
      taskList.Add(voteTask);
      taskList.Add(replyTask);
    }
    await Task.WhenAll(taskList);

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

  protected abstract Entry CreateLogEntry(int Index, int Term, object Data);

  protected void HandleUserRequest(object Data)
  {
    // TODO: batch SendAppendEntries call
    if (nodeState == State.Follower)
    {
      RedirectUserRequest(leaderId, Data);
      return;
    }
    if (nodeState == State.Candidate)
    {
      // TODO
      return;
    }

    commitIndex++;
    Entry entry = CreateLogEntry(commitIndex, CurrentTerm, Data);
    log.Add(entry);

    Entry prevLog = log[^1];
    foreach (int followerId in nodeIds)
    {
      SendAppendEntries(followerId, CurrentTerm, id, prevLog.Index, prevLog.Term, [entry], commitIndex);
    }
  }
}