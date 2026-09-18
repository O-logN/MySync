using System.Globalization;
using System.Net.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Identity.Client;

static class Logger
{
	enum Level { Success, Info, Error };
	static readonly Lock _consoleLock = new();
	internal static bool WasPrintErrorCalled { get; set; } = false;

    internal static void PrintSuccess(string? sender, string msg)
        => PrintMsg(Level.Success, sender, msg);

    internal static void PrintInfo(string? sender, string msg)
        => PrintMsg(Level.Info, sender, msg);

    internal static void PrintError(string sender, string msg)
    {
		WasPrintErrorCalled = true;
		PrintMsg(Level.Error, sender, msg);
		Console.Beep();
	}

    internal static void PrintError(string sender, Exception ex)
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
		using (_consoleLock.EnterScope()) // [1]
        {
			Console.Write(DateTime.Now.ToString(new CultureInfo("it-IT")));
			Console.Write("\t");
			
            var startColor = Console.ForegroundColor;
			Console.ForegroundColor = level switch
			{
				Level.Success => ConsoleColor.Green,
				Level.Info => startColor,
				Level.Error => ConsoleColor.Red,
				_ => throw new ArgumentException($"Unsupported level: {level}")
			};
			Console.Write($"[{level}]");
			Console.ForegroundColor = startColor;

			if (sender is not null)
				Console.Write($" reported by {sender}");
			Console.Write($":\t{msg}\n");
        }
    }
}

/*
	[1]
		So that two different PrintMsg() calls cannot mix up.
*/