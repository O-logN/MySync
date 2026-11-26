using System.Threading.Channels;
using MySync.Data;
using MySync.Data.Operations;
using MySync.Utilities;

namespace MySync.Uploading;

public class Uploader
{
    readonly Channel<Snapshot> _snapshotChannel;
    readonly Fetcher _fetcher;
    readonly Requestor _requestor;

    Uploader(Channel<Snapshot> snapshotChannel, Fetcher fetcher, Requestor requestor)
    {
        _snapshotChannel = snapshotChannel;
        _fetcher = fetcher;
        _requestor = requestor;
    }

    public static async Task<Uploader?> TryCreateAsync(ChannelWriter<DbOperation> operationWriter)
    {
        var snapshotChannel = Channel.CreateUnbounded<Snapshot>();
        var fetcher = new Fetcher(operationWriter, snapshotChannel.Writer);
        var requestor = await Requestor.TryCreateAsync(snapshotChannel.Reader, operationWriter);
        return requestor is not null ? new Uploader(snapshotChannel, fetcher, requestor) : null;
    }

    public async Task RunAsync(CancellationToken cToken)
    {
        var fetcherTask = _fetcher.RunAsync(cToken);
        await fetcherTask;
        _snapshotChannel.Writer.Complete();
    }

    // chiamare Requestor l'altro
    // leggere signature, nel fetcher mettere il dizionario, semmai fargli fare chiamate asincrone
    //public static async Task<Uploader?> TryCreateAsync(ChannelWriter<DbOperation> operationWriter)
   // {
        
        /*
        var eventChannel = Channel.CreateUnbounded<FileSystemEventArgs>();
        var watcher = new Watcher(eventChannel.Writer);
        var evaluator = await Evaluator.TryCreateAsync(eventChannel.Reader, operationWriter);
        return evaluator is not null ? new Observer(eventChannel, watcher, evaluator) : null;
        */
   // }
    
}