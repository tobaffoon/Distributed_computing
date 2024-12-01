namespace RafRaft.RaftMap
{
   using RafRaft.Domain;

   public class RaftMapStateMachine<T> : IRaftStateMachine<KeyValuePair<string, T>, T>
      where T : notnull
   {
      public Dictionary<string, T> State { get; } = [];

      public void Apply(RaftLogEntry<KeyValuePair<string, T>> entry)
      {
         State[entry.Data.Key] = entry.Data.Value;
      }

      public T RequestData(string param)
      {
         return State[param];
      }
   }
}