namespace RafRaft.Domain.Messages
{
   public record VoteRequest(int Term, int CandidateId, int LastLogId, int LastLogTerm);
}
