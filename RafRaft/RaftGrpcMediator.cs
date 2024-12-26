namespace RafRaft
{
   using RafRaft.Domain;

   public interface RaftGrpcMediator<TDataIn, TGrpcClient> : IRaftMediator<TDataIn>
        where TDataIn : notnull
        where TGrpcClient : class
   {
      public IReadOnlyDictionary<int, TGrpcClient> Clients { get; }
      public Dictionary<int, string> Names { get; }
   }
}

