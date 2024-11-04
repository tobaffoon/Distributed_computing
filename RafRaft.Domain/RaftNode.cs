using System;

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
}
