using System;

namespace RafRaft.Domain;

public record RaftLogEntry<T>(int Index, int Term, Action<T> action);