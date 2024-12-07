namespace RafRaft.Domain;

public record class RaftNodeConfig(int Id, long BroadcastTime, int MinElectionMillis, int MaxElectionMillis, IList<int> PeersIds);