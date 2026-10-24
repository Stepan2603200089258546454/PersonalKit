using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Common.Helper.Http.Tests.Infrastructure;

/// <summary>
/// Содержимое ответа, которое запоминает факт освобождения: нужно, чтобы проверить,
/// что промежуточные ответы при повторах освобождаются.
/// </summary>
internal sealed class TrackingContent : HttpContent
{
    private int _disposeCount;

    public int DisposeCount => Volatile.Read(ref _disposeCount);

    public bool IsDisposed => DisposeCount > 0;

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => Task.CompletedTask;

    protected override bool TryComputeLength(out long length)
    {
        length = 0;
        return true;
    }

    protected override void Dispose(bool disposing)
    {
        Interlocked.Increment(ref _disposeCount);
        base.Dispose(disposing);
    }
}
