using System.Threading.Channels;
using MySync.Data.Operations;
using MySync.Utilities;

namespace MySync.Monitoring;

class Observer : IDisposable // [1]
{
    readonly Channel<FileSystemEventArgs> _eventChannel; // [2]
    readonly Watcher _watcher;
    readonly Evaluator _evaluator;

    Observer(Channel<FileSystemEventArgs> eventChannel, Watcher watcher, Evaluator evaluator)
    {
        _eventChannel = eventChannel;
        _watcher = watcher;
        _evaluator = evaluator;
    }

    public static async Task<Observer?> TryCreateAsync(ChannelWriter<DbOperation> operationWriter)
    {
        var eventChannel = Channel.CreateUnbounded<FileSystemEventArgs>();
        var watcher = new Watcher(eventChannel.Writer);
        var evaluator = await Evaluator.TryCreateAsync(eventChannel.Reader, operationWriter);
        return evaluator is not null ? new Observer(eventChannel, watcher, evaluator) : null;
    }

    public async Task RunAsync(CancellationToken cToken)
    {
        _watcher.Start();
        var evaluatorTask = _evaluator.RunAsync();
        await TaskEx.TryDelay(Timeout.InfiniteTimeSpan, cToken);
        _watcher.Stop();
        await Task.Delay(TimeSpan.FromMilliseconds(200)); // [3]
        _eventChannel.Writer.Complete();
        await evaluatorTask;
    }

    public void Dispose()
    {
        _watcher.Dispose();
    }
}

/*
    [1]
        The Watcher stops monitoring the filesystem if a BufferOverflowException happens.
        The Evaluator stops evaluating events if it cant reload the rules (but it still consumes them).
        In each case LogUtils.PrintError() is called, so a beep sound is played and neither death is silent.
        Yet, the Observer is never notified of any. As long as each occurs very rarely (if ever), I prefer to
        save time and code complexity rather than implementing some retry mechanism (so, restarting the application
        is needed).
    
    [2]
        This channel allows the Watcher to communicate with the Evaluator. But why a channel? I think that
        it emphasizes the separation between the two components. Also, it might provide better performances than
        an asynchronous call. That is relevant in Watcher.OnNotError(), as Microsoft warns that the method
        invoked because of an event should take as little time as possible (in order to reduce the risk of buffer
        overflows).
    
    [3]
        A grace period to let pending OnNotError()s within the Watcher end. I chose the duration at random, so it might
        not be enough. However, it's small enough not to be noticeable by the user, so better than nothing.
        Also check comment [3] in Watcher.cs
*/