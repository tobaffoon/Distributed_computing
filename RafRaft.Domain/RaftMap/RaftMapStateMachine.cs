namespace RafRaft.Domain;

public class RaftMapStateMachine : IRaftStateMachine<Dictionary<string, object>, object>
{
   public Dictionary<string, object> State { get; } = [];
   public void Apply(RaftLogEntry<Dictionary<string, object>> logEntry)
   {
      foreach (var entry in logEntry.Data)
      {
         State[entry.Key] = entry.Value;
      }
   }

   public object RequestData(string param)
   {
      return State[param];
   }
}
