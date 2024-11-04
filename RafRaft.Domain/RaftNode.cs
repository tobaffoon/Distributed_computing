using System;
using System.Diagnostics.Contracts;
using System.Net.Sockets;

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
  protected State state = State.Follower;
  protected int currentTerm = 0;
  protected int? votedFor;
  protected abstract IEnumerable<Action<T>> Log
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
  protected readonly long broadcastTime;
  protected readonly long electionTimeout;
  protected readonly int[] peersIds;
  protected int ClusterSize => peersIds.Length + 1;

  public RaftNode(long BroadcastTime, long ElectionTimeout, IEnumerable<int> PeersIds)
  {
    broadcastTime = BroadcastTime;
    electionTimeout = ElectionTimeout;
    peersIds = PeersIds.ToArray();
  }

  public abstract (int, bool) SendAppendEntries(int Term, int LeaderId, int PrevLogIndex,
    int prevLogTerm, IEnumerable<Action<T>>? Entries, int LeaderCommit);
  public abstract (int, bool) SendRequestVote(int Term, int CandidateId,
    int LastLogIndex, int LastLogTerm);
  public abstract void HandleAppendEntries(int Term, int LeaderId, int PrevLogIndex,
    int prevLogTerm, IEnumerable<Action<T>>? Entries, int LeaderCommit);
  public abstract void HandleRequestVote(int Term, int CandidateId,
    int LastLogIndex, int LastLogTerm);
}