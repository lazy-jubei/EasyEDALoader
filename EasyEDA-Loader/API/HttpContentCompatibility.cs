#if ALTIUM17
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace EasyEDA_Loader
{
    internal static class HttpContentCompatibility
    {
        // Requests use ResponseContentRead, so the request's token already covers
        // downloading the body. Framework's content readers have no token overload.
        internal static async Task<string> ReadAsStringAsync(this HttpContent content, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var result = await content.ReadAsStringAsync().ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            return result;
        }

        internal static async Task<byte[]> ReadAsByteArrayAsync(this HttpContent content, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var result = await content.ReadAsByteArrayAsync().ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            return result;
        }
    }
}
#endif
