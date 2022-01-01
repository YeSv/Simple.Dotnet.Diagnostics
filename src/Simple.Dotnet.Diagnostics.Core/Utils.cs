using Simple.Dotnet.Utilities.Results;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Simple.Dotnet.Diagnostics.Core;

public static class ResultsExtensions
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValueTask<Result<TOk, TError>> AsValueTask<TOk, TError>(this in Result<TOk, TError> result) => new(result);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValueTask<UniResult<TOk, TError>> AsValueTask<TOk, TError>(this in UniResult<TOk, TError> result) where TOk : class where TError : class => new(result);
}

