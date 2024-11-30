namespace RafRaft.Domain;

public abstract record RaftLogEntry<TDataIn, TDataOut>
   where TDataIn : struct
   where TDataOut : struct
{
   public int Index { get; init; }
   public int Term { get; init; }
   public required TDataIn Data { get; init; }

   public abstract void Apply(IRaftStateMachine<TDataOut> InternalState);

   public virtual bool Equals(RaftLogEntry<TDataIn, TDataOut>? Entry)
   {
      if (Entry is null) return false;

      return Data.Equals(Entry.Data);
   }
   public override int GetHashCode()
   {
      return Data.GetHashCode();
   }
}