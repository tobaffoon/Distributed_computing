namespace RafRaft.Domain;

public interface IRaftStateMachine<TDataIn, TDataOut>
   where TDataIn : notnull
   where TDataOut : notnull
{
   /// <summary>
   /// Applies changes to the state machine, changes are specified in log entry. 
   /// </summary>
   void Apply(RaftLogEntry<TDataIn> logEntry);

   /// <summary>
   /// Finds the subsection of the state machine, limited by the given command.
   /// </summary>
   /// <param name="param">Limits, filters or specifies the information to retrieve.</param>
   /// <returns>A struct containing the specified representation of the state machine</returns>
   TDataOut RequestData(string param);

   /// <summary>
   /// Used to determine the broadcast time.
   /// The method must implement a dummy request to the state machine, in order to correctly approximate the broadcast time.
   /// </summary>
   void TestConnection();
}
