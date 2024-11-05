using System;

namespace RafRaft.Domain;

public abstract record RaftLogEntry<T, D>(int Index, int Term, D Data) where D : struct
{
  public virtual bool Equals(RaftLogEntry<T, D>? Entry)
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