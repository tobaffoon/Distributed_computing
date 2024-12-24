namespace RafRaft.Domain.Messages
{
   public record VoteReply(int Term, bool VoteGranted);
}