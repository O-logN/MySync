using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.Data.Sqlite;
using Microsoft.Identity.Client;

static class Logger
{
	internal static bool WasPrintErrorCalled { get; set; } = false; // [1]
	enum Level { Success, Info, Error };
	static readonly Lock _consoleLock = new();

    internal static void PrintSuccess(string msg)
        => PrintMsg(Level.Success, null, msg);

    internal static void PrintInfo(string msg)
        => PrintMsg(Level.Info, null, msg);

    internal static void PrintError<T>(this T caller, string msg, [CallerMemberName] string memberName = "") where T : notnull // [2]
		=> PrintError($"{caller.GetType()}.{memberName}()", msg);

    internal static void PrintError<T>(this T caller, Exception ex, [CallerMemberName] string memberName = "") where T : notnull
		=> PrintError($"{caller.GetType()}.{memberName}()", ex);

    static void PrintError(string sender, string msg)
    {
		WasPrintErrorCalled = true;
		PrintMsg(Level.Error, sender, msg);
		Console.Beep();
	}

    static void PrintError(string sender, Exception ex)
    {
        WasPrintErrorCalled = true;
		string moreInfo = ex switch
		{
			MsalException msalEx => $"[MSAL ErrorCode: {msalEx.ErrorCode}]",
			HttpRequestException httpEx => $"[HTTP RequestError: {httpEx.HttpRequestError}]",
			SqliteException sqliteEx => $"[SQLite ExtendedErrorCode: {sqliteEx.SqliteExtendedErrorCode}]",
			_ => ""
		};
        PrintMsg(Level.Error, sender, $"{ex.GetType().Name}\t-\t{ex.Message}\t-\t{moreInfo}");
		Console.Beep();
    }

    static void PrintMsg(Level level, string? sender, string msg)
    {
		using (_consoleLock.EnterScope()) // [3]
        {
			Console.Write(DateTime.Now.ToString("T", new CultureInfo("it-IT")));
			Console.Write("\t");
			
            var startColor = Console.ForegroundColor;
			Console.ForegroundColor = level switch
			{
				Level.Success => ConsoleColor.Green,
				Level.Info => ConsoleColor.Yellow,
				Level.Error => ConsoleColor.Red,
				_ => throw new ArgumentException($"Unsupported level: {level}")
			};
			Console.Write($"[{level}]");
			Console.ForegroundColor = startColor;

			if (sender is not null)
				Console.Write($" reported by {sender}");
			Console.Write($"\t{msg}\n");
        }
    }
}

/*
	[1]
		This is to avoid making the console close if an error was logged, despite "q" having been entered
		(see Program.cs).

	[2]
		https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/attributes/caller-information
		
	[3]
		So that two different PrintMsg() calls cannot mix up.
*/