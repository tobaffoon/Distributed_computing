using System;
using System.Net;

namespace RafRaft;

public record RaftGrpcNodeConfig(IPEndPoint EndPoint, int Id);