namespace RafRaft
{
   using System.Collections.ObjectModel;
   using RafRaft.Domain;

   public interface RaftGrpcMediator<TDataIn, TGrpcClient> : IRaftMediator<TDataIn>
        where TDataIn : notnull
        where TGrpcClient : class
   {
      ReadOnlyDictionary<int, TGrpcClient> Clients { get; }
   }
}

