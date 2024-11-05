using System;
using RafRaft.Domain;

namespace RafRaft;

public record RaftMapEntry<K, V> : RaftLogEntry<Dictionary<K, V>> where K : notnull
{
  public RaftMapEntry(int Index, int Term, (K, V) Data) : base(Index, Term, Data) { }
  public (K, V) KvData => ((K, V))Data;
  public override void Apply(Dictionary<K, V> InternalState)
  {
    InternalState[KvData.Item1] = KvData.Item2;
  }
}
