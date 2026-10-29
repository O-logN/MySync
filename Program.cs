using System.Threading.Channels;
using DbOperations;

class Program
{
	static async Task Main()
	{
		using var cTokenSource = new CancellationTokenSource();
		
		using var watcher = new Watcher();
		var evaluator = new Evaluator();
		var interactor = new Interactor();
		var provider = await Provider.CreateAsync();
		var uploader = await Uploader.CreateAsync(cTokenSource.Token); // [1] , [2]
		
		var eventChannel = Channel.CreateUnbounded<Event>(); // [3]
		var operationChannel = Channel.CreateUnbounded<DbOperation>();

		var interactorTask = interactor.RunAsync(operationChannel.Reader, cTokenSource.Token); // [4]
		operationChannel.Writer.TryWrite(new InitDb());
		var evaluatorTask = evaluator.RunAsync(eventChannel.Reader, operationChannel.Writer, cTokenSource.Token);
		var uploaderTask = uploader.RunAsync(operationChannel.Writer, provider, cTokenSource.Token);
		watcher.Start(eventChannel.Writer);
		
		bool shouldRun = true;
		Logger.PrintInfo("Enter \"h\" to print the menu.");
		while (shouldRun)
		{
			switch (Console.ReadLine())
			{
				case "h":
					Logger.PrintInfo("\n"+
					"""
						h -> print menu
						w -> wake up the Uploader
						q -> quit
					""");
					break;
					
				case "w":
					uploader.StopSuspension();
					break;
			
				case "q":
					watcher.Stop();
					eventChannel.Writer.Complete();
					cTokenSource.Cancel();
					await evaluatorTask;
					operationChannel.Writer.Complete();
					await interactorTask;
					uploader.StopSuspension();
					await uploaderTask;
					shouldRun = false;
					break;
			}
		}
		if (Logger.WasPrintErrorCalled)
		{
			Logger.PrintInfo("\nPress any key to continue\n");
			Console.ReadKey(true);
		}
	}
}

/*
	[1]
		Despite the presence of a cancellation token, the task is not interruptible. This is because cToken
		can be canceled only if the user types "q" on the console, but the "await" blocks the current method.
		This is not a problem as the task will end within a few seconds.
	
	[2]
		If the Uploader cannot read signature.txt (despite retries), then an exception is thrown. Note that
		failing to read the signature does not prevent the whole program to function, but for simplicity I still let
		it end.

	[3]
		In this program I only use unbounded channels.
		So, TryComplete() always succeeds unless the channel got completed. Check:
			https://learn.microsoft.com/en-us/dotnet/core/extensions/channels#:~:text=used%20with%20an-,unbounded%20channel,-%2C%20this%20always%20returns

	[4]
		This order ensures that the Main() thread returns immediately from any RunAsync(), so that it can immediately
		enter the console loop.
*/