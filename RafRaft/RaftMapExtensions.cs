namespace RafRaft
{
   using RafRaft.Domain.Messages;
   using RafRaft.Protos;

   using LogEntry = Domain.RaftLogEntry<KeyValuePair<string, Protos.Data>>;

   public static class RaftMapExtensions
   {
      #region VoteRequest
      public static VoteMapRequest ConvertToGrpc(this VoteRequest request)
      {
         VoteMapRequest grpcRequest = new VoteMapRequest()
         {
            Term = request.Term,
            CandidateId = request.CandidateId,
            LastLogId = request.LastLogId,
            LastLogTerm = request.LastLogTerm
         };

         return grpcRequest;
      }

      public static VoteRequest ConvertFromGrpc(this VoteMapRequest grpcRequest)
      {
         VoteRequest request = new VoteRequest(
            grpcRequest.Term,
            grpcRequest.CandidateId,
            grpcRequest.LastLogId,
            grpcRequest.LastLogTerm
         );

         return request;
      }

      public static VoteReply ConvertFromGrpc(this VoteMapReply grpcReply)
      {
         VoteReply reply = new VoteReply(grpcReply.Term, grpcReply.VoteGranted);

         return reply;
      }

      public static VoteMapReply ConvertToGrpc(this VoteReply reply)
      {
         VoteMapReply grpcReply = new VoteMapReply()
         {
            Term = reply.Term,
            VoteGranted = reply.VoteGranted
         };

         return grpcReply;
      }
      #endregion

      #region AppendEntries
      public static AppendMapEntriesRequest ConvertToGrpc(this AppendEntriesRequest<KeyValuePair<string, Data>> request)
      {
         AppendMapEntriesRequest grpcRequest = new AppendMapEntriesRequest()
         {
            Term = request.Term,
            LeaderId = request.LeaderId,
            PrevLogId = request.PrevLogId,
            PrevLogTerm = request.PrevLogTerm,
            LeaderCommitId = request.LeaderCommitId
         };
         PopulateProtobufMap(grpcRequest, request);

         return grpcRequest;
      }

      public static AppendEntriesRequest<KeyValuePair<string, Data>> ConvertFromGrpc(this AppendMapEntriesRequest grpcRequest)
      {
         var request = new AppendEntriesRequest<KeyValuePair<string, Data>>(
            grpcRequest.Term,
            grpcRequest.LeaderId,
            grpcRequest.PrevLogId,
            grpcRequest.PrevLogTerm,
            [],
            grpcRequest.LeaderCommitId
         );
         PopulateMessage(request, grpcRequest);

         return request;
      }

      public static AppendEntriesReply ConvertFromGrpc(this AppendMapEntriesReply grpcReply)
      {
         AppendEntriesReply reply = new AppendEntriesReply(grpcReply.Term, grpcReply.Success);

         return reply;
      }

      public static AppendMapEntriesReply ConvertToGrpc(this AppendEntriesReply reply)
      {
         AppendMapEntriesReply grpcReply = new AppendMapEntriesReply()
         {
            Term = reply.Term,
            Success = reply.Success
         };

         return grpcReply;
      }

      private static void PopulateProtobufMap(AppendMapEntriesRequest grpcRequest, AppendEntriesRequest<KeyValuePair<string, Data>> request)
      {
         foreach (LogEntry logEntry in request.Entries)
         {
            LogMapEntry grpcEntry = new LogMapEntry()
            {
               Term = logEntry.Term,
               Index = logEntry.Index,
               Key = logEntry.Data.Key,
               Value = logEntry.Data.Value
            };

            grpcRequest.Entries.Add(grpcEntry);
         }
      }

      private static void PopulateMessage(AppendEntriesRequest<KeyValuePair<string, Data>> request, AppendMapEntriesRequest grpcRequest)
      {
         foreach (LogMapEntry grpcEntry in grpcRequest.Entries)
         {
            LogEntry entry = new LogEntry(
               grpcEntry.Term,
               grpcEntry.Index,
               new KeyValuePair<string, Data>(grpcEntry.Key, grpcEntry.Value));

            request.Entries.Add(entry);
         }
      }
      #endregion
   }
}