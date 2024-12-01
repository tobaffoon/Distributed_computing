namespace RafRaft
{
   using System;
   using System.Net;

   public record RaftGrpcNodeConfig(IPEndPoint EndPoint, int Id);
}