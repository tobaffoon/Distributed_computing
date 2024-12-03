namespace RafRaft.Domain.Messages
{
   public record AppendEntriesReply(int Term, bool Success);
}
