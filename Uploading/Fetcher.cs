using System.Threading.Channels;
using MySync.Data;
using MySync.Data.Operations;
using MySync.Utilities;

namespace MySync.Uploading;

class Fetcher(ChannelWriter<DbOperation> operationWriter, ChannelWriter<Snapshot> snapshotWriter)
{
    readonly TimeSpan _initialDelay = TimeSpan.FromMilliseconds(1);// TimeSpan.FromMinutes(5); // [1]
    readonly TimeSpan _iterationDelay = TimeSpan.FromMinutes(1);
    
    public async Task RunAsync(CancellationToken cToken)
    {
        await TaskEx.TryDelay(_initialDelay, cToken);
        while (!cToken.IsCancellationRequested)
        {
            var pathEntries = await TryGetPathEntries();
            if (pathEntries is null)
                break;
            var tcs = new TaskCompletionSource();
            snapshotWriter.TryWrite(new Snapshot(pathEntries, tcs));
            await Task.WhenAll(
                TaskEx.TryDelay(_iterationDelay, cToken),
                tcs.Task.TryWaitAsync(cToken)
            );
        }
    }

    async Task<PathEntry[]?> TryGetPathEntries()
    {
        var tcs = new TaskCompletionSource<PathEntry[]>();
        operationWriter.TryWrite(new GetPaths(tcs));
        try
        {
            return await tcs.Task;
        }
        catch(OperationCanceledException)
        {
            return null;
        }
    }
}

/*
    [1]
        I have my computer run this program upon startup. I need to manually set up the connection, hence the delay.
*/