using System.Runtime.CompilerServices;
using System.Threading;
using Corax.Querying.Matches.Meta;
using Corax.Querying.Matches.SortingMatches;
using Corax.Utils;

namespace Corax.Querying;

public unsafe partial class IndexSearcher
{
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SortingMatch OrderBy<TInner>(in TInner set, OrderMetadata metadata, int take = Constants.IndexSearcher.TakeAll, in CancellationToken token = default)
        where TInner : IQueryMatch
    {
        return SortingMatch.Create(new SortingMatch<TInner>(this,  set, metadata, token, take));
    }
    
        
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SortingMultiMatch OrderBy<TInner>(in TInner set, OrderMetadata[] metadata,
        int take = Constants.IndexSearcher.TakeAll, in CancellationToken token = default)
        where TInner : IQueryMatch
    {
        return SortingMultiMatch.Create(new SortingMultiMatch<TInner>(this,  set, metadata, take, token: token));
    }
}
