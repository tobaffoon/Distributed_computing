namespace RafRaft.Domain.Messages
{
   public record AppendEntriesRequest<TDataIn>(int Term, int LeaderId, int PrevLogId, int PrevLogTerm, IList<RaftLogEntry<TDataIn>> Entries, int LeaderCommitId)
      where TDataIn : notnull;
}
