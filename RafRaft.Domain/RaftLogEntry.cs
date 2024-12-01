namespace RafRaft.Domain;

public record RaftLogEntry<TDataIn>(int Index, int Term, TDataIn Data)
   where TDataIn : notnull
{
   public virtual bool Equals(RaftLogEntry<TDataIn>? Entry)
   {
      if (Entry is null) return false;

      return Data.Equals(Entry.Data);
   }

   public override int GetHashCode()
   {
      return Data.GetHashCode();
   }
}