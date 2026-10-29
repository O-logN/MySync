static class FileManager
{	// [1]
	internal static string SyncPath { get; } = Path.Combine(GetFolderPath(Environment.SpecialFolder.MyDocuments), "Sync");
	internal static string MySyncPath { get; } = Path.Combine(GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MySync");
	internal static string DbPath { get; } = Path.Combine(MySyncPath, "db.db");
	internal static string SignaturePath { get; } = Path.Combine(MySyncPath, "signature.txt");
	internal static string CacheFileName { get; } = "cache.dat";
	
	static string GetFolderPath(Environment.SpecialFolder folder)
	{
		var folderPath = Environment.GetFolderPath(folder);
		return folderPath != "" ? folderPath : throw new InvalidOperationException($"{folder} does not physically exist.");
	}

	internal static async Task<string> ReadFileAsync(string path, CancellationToken cToken)
	{
		using var fs = await OpenFileAsync(path, FileMode.Open, FileAccess.Read, cToken);
		using var reader = new StreamReader(fs);
		return reader.ReadToEnd();
	}
	
	internal static async Task<FileStream> OpenFileAsync(string path, FileMode fileMode, FileAccess fileAccess, CancellationToken cToken)
	{
		const int maxRetries = 5, numMs = 500;
		for (int i = 0; i < maxRetries; i++)
		{
			try
				{ return File.Open(path, fileMode, fileAccess); }
			catch (IOException ex) when (((ex.HResult & 0x0000FFFF) == 32) || ((ex.HResult & 0x0000FFFF) == 33)) // [2]
				{ await Task.Delay(numMs, cToken); }
		}
		throw new IOException($"Couldn\'t open {path} using {fileMode} fileMode and {fileAccess} fileAccess despite {maxRetries} attempts");
	}
}

/*
	[1]
		Each of the following two assignments may throw an InvalidOperationException. If one does, then a
		TypeInitializationException encapsulating that InvalidOperationException will be thrown.
			https://learn.microsoft.com/en-us/dotnet/api/system.typeinitializationexception?view=net-10.0
		SyncPath is needed to initialize the Watcher, while MySyncPath is needed to initialize the Uploader.
		At one of those points, if a TypeInitializationException is thrown then the program will crash.
		It's of no use to catch the exception (besides for some pretty-printing) as it implies that the program
		is in an unrecoverable state. Anyway, note that one such exception is thrown only if Documents or AppData\Local
		dont physically exist:
			https://learn.microsoft.com/en-us/dotnet/api/system.environment.getfolderpath?view=net-10.0
		which doesnt seem very likely.

	[2]
		Another process is using the file. Useful links:
			https://learn.microsoft.com/en-us/dotnet/standard/io/handling-io-errors
			https://learn.microsoft.com/en-us/windows/win32/debug/system-error-codes--0-499-
*/