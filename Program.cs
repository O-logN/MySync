using System.Threading.Channels;
using MySync.Monitoring;
using MySync.Data;
using MySync.Data.Operations;
using MySync.Uploading;
using MySync.Utilities;

// sistemare il commento 3 in Evaluator.cs, mettere il "see x.cs" e scrivere in tale x.cs perchè l'ordinamento serve
// anche il quarto
// rinominare OpenFileAsync in OpenAsync ? e anche TryReadFile; fai un findreplace per colpire anche i commenti

class Program // [0]
{
	static async Task Main()
	{
		_ = FileUtils.SyncPath; // [1]
		using var cTokenSource = new CancellationTokenSource();
		var operationChannel = Channel.CreateUnbounded<DbOperation>();

		using var observer = await Observer.TryCreateAsync(operationChannel.Writer);
		if (observer is null) return;
		var uploader = new Uploader(operationChannel.Writer);
		var interactor = new Interactor(operationChannel.Reader);

		var interactorTask = interactor.RunAsync(); // [2]
		operationChannel.Writer.TryWrite(new InitDb());
		var observerTask = observer.RunAsync(cTokenSource.Token);
		var uploaderTask = uploader.RunAsync(cTokenSource.Token);

		LogUtils.PrintInfo("Enter \"h\" to print the menu.");
		while (!cTokenSource.Token.IsCancellationRequested)
		{
			switch (Console.ReadLine())
			{
				case "h":
					LogUtils.PrintInfo("\n"+
					"""
						h -> print menu
						q -> quit
					""");
					break;
			
				case "q": // [3]
					cTokenSource.Cancel();
					await observerTask;
					await uploaderTask;
					operationChannel.Writer.Complete();
					await interactorTask;
					break;
			}
		}
		if (LogUtils.WasPrintErrorCalled)
		{
			Console.WriteLine("Type any key to quit");
			Console.ReadKey();
		}
	}
}

/*
	[0]
		In this code I tend to use the "TryDoSomething()" pattern, with the callee logging (the error) and returning
		either null or false to the caller if an error occurs. If the caller only needs to know the result, or that
		an error occurred but not which in particular, then I think the code benefits from this pattern.

	[1]
		Triggering the initialization of FileUtils' properties, so that if a TypeInitialization exception is
		to be thrown, then now is the moment. I chose to let each class access those properties because I believe
		it makes the code leaner, even though "less reusable".
	
	[2]
		Starting the Interactor before the other components ensures that the current thread immediately quits it
		(since there is an await foreach on an empty channel).

	[3]
		Starts a best-effort and graceful shutdown.
*/