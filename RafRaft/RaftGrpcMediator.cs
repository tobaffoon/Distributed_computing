namespace RafRaft
{
   using RafRaft.Domain;

   public interface RaftGrpcMediator<TDataIn, TGrpcClient> : IRaftMediator<TDataIn>
        where TDataIn : notnull
        where TGrpcClient : class
   {
      IReadOnlyDictionary<int, TGrpcClient> Clients { get; }
   }
}

