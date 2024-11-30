namespace RafRaft.Domain;

public interface IRaftStateMachine<TDataOut>
   where TDataOut : struct
{
   /// <summary>
   /// Finds the string representation of the state machine, limited by the given command.
   /// </summary>
   /// <param name="param">Limits, filters or specifies the information to retrieve.</param>
   /// <returns>A string containing the specified representation of the state machine</returns>
   TDataOut RequestData(string param);

   /// <summary>
   /// Used to determine the broadcast time.
   /// The method must implement a dummy request to the state machine, in order to correctly approximate the broadcast time.
   /// </summary>
   void TestConnection();
}
