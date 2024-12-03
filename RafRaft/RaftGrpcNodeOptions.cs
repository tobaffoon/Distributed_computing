using System;

namespace RafRaft;

public sealed class RaftGrpcNodeOptions
{
   public required int Id { get; set; }
   public required string Address { get; set; }
}
