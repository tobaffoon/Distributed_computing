namespace RafRaft.Domain;

public record class RaftNodeConfig(int Id, long BroadcastTime, long ElectionTimeout, IList<int> NodeIds);