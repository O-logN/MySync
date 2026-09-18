using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using DbOperations;

class Program // [0]
{
	static async Task Main()
	{
		using var cTokenSource = new CancellationTokenSource();
		
		using var watcher = new Watcher();
		var evaluator = new Evaluator();
		var interactor = new Interactor();
		var provider = await Provider.CreateAsync();
		var uploader = await Uploader.CreateAsync(cTokenSource.Token); // [3]
		
		var eventChannel = Channel.CreateUnbounded<FileSystemEventArgs>(); // [1]
		var operationChannel = Channel.CreateUnbounded<DbOperation>();

		var interactorTask = interactor.RunAsync(operationChannel.Reader, cTokenSource.Token); // [2]
		operationChannel.Writer.TryWrite(new InitDb());
		var evaluatorTask = evaluator.RunAsync(eventChannel.Reader, operationChannel.Writer, cTokenSource.Token);
		var uploaderTask = uploader.RunAsync(operationChannel.Writer, provider, cTokenSource.Token);
		watcher.Start(eventChannel.Writer);

		bool shouldRun = true;
		Logger.PrintInfo(null, "Enter \"h\" to print the menu.");
		while (shouldRun)
		{
			switch (Console.ReadLine())
			{
				case "h":
					Logger.PrintInfo(null,"\n"+
					"""
						h -> print menu
						s -> stop or prevent Uploader suspension
						q -> quit\n
					""");
					break;
					
				case "s":
					uploader.StopSuspension();
					break;
			
				case "q":
					watcher.Stop();
					eventChannel.Writer.Complete();
					await evaluatorTask;
					operationChannel.Writer.Complete();
					cTokenSource.Cancel();
					await interactorTask;
					if (!uploader.HasFinished)
						uploader.StopSuspension();
					await uploaderTask;
					shouldRun = false;
					break;
			}
		}
		if (Logger.WasPrintErrorCalled)
		{
			Logger.PrintInfo(null, "\nPress any key to continue\n");
			Console.ReadKey(true);
		}
	}
}

/*
	[0]
		to be done.

	[1]
		In this program, we only use unbounded channels.
		So, TryComplete() always succeeds unless the channel got completed. Check:
			https://learn.microsoft.com/en-us/dotnet/core/extensions/channels#:~:text=used%20with%20an-,unbounded%20channel,-%2C%20this%20always%20returns

	[2]
		This order ensures that the Main() thread returns immediately from any RunAsync(), so that it can immediately
		listen to the console.

	[3]
		Despite the presence of a cToken, the task is not interruptible. That's just because the method execution
		gets blocked. So, the user has either to wait that FileManager.OpenWithRetries() gives up (requires some seconds)
		or to force the program to close. The latter seems ok, because except for the cTokenSource there are not resources
		that should be disposed (I suppose that not disposing cTokenSource is not a problem). I could have made the
		Uploader creation be asynchronous, but that would have resulted in more difficult code (that doesnt seem to be
		worth the effort).
*/