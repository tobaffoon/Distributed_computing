namespace RafRaft.Domain
{
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
      /// <param name="userRequest">Limits, filters or specifies the information to retrieve.</param>
      /// <returns>A struct containing the specified representation of the state machine</returns>
      TDataOut RequestData(string userRequest);
   }
}