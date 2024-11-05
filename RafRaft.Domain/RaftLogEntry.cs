using System;

namespace RafRaft.Domain;

public abstract record RaftLogEntry<T>(int Index, int Term, object Data)
{
  public virtual bool Equals(RaftLogEntry<T>? Entry)
  {
    if (Entry is null) return false;
    return Data.Equals(Entry.Data);
  }
  public override int GetHashCode()
  {
    return Data.GetHashCode();
  }
  public abstract void Apply(T InternalState);
}