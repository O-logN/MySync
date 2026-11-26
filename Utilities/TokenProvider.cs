using System.Runtime.InteropServices;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Broker;
using Microsoft.Identity.Client.Extensions.Msal;

namespace MySync.Utilities;

public class TokenProvider
{
	const string ClientId = "";
	readonly string[] _scopes = ["User.Read", "Files.ReadWrite"]; // [5]
	readonly PublicClientApplication _app;
	
	TokenProvider()
	{
		_app = (PublicClientApplication)
				PublicClientApplicationBuilder.Create(ClientId) // [1]
				.WithParentActivityOrWindow(GetConsoleOrTerminalWindow)
				.WithAuthority(AadAuthorityAudience.PersonalMicrosoftAccount)
				.WithBroker(new BrokerOptions(BrokerOptions.OperatingSystems.Windows))
				.Build();
	}
	
	public static async Task<TokenProvider> CreateAsync() // [2]
	{
		var tokenProvider = new TokenProvider(); // [3]
		var tmp = await MsalCacheHelper.CreateAsync(
			new StorageCreationPropertiesBuilder(FileUtils.CacheFileName, FileUtils.MySyncPath).Build()
		);
		tmp.RegisterCache(tokenProvider._app.UserTokenCache);
		return tokenProvider;
	}

	public async Task<string?> TryGetAccessTokenAsync(CancellationToken cToken) // [4]
	{
		try
		{
			IAccount? account = (await _app.GetAccountsAsync(cToken)).FirstOrDefault(); // [6]
			if (account is not null)
			{
				try
				{
					return (await _app.AcquireTokenSilent(_scopes, account).ExecuteAsync(cToken)).AccessToken;
				}
				catch (MsalUiRequiredException) // [7]
				{
				}
			}
			var tmp = await _app.AcquireTokenInteractive(_scopes).WithPrompt(Prompt.SelectAccount).ExecuteAsync(cToken);
			if ((await _app.GetAccountsAsync(cToken)).Count() > 1)
			{
				LogUtils.PrintError("Another account is already in cache.");
				return null;
			}
			return tmp.AccessToken;
		}
		catch (MsalException ex) when (ex.ErrorCode is MsalError.AuthenticationCanceledError) // [10]
		{
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception e) // [11]
		{
			LogUtils.PrintError(e);
		}
		return null;
	}
	
	// [9]
	enum GetAncestorFlags
	{   
		GetParent = 1,
		GetRoot = 2,
		GetRootOwner = 3
	}

	[DllImport("user32.dll", ExactSpelling = true)]
	static extern IntPtr GetAncestor(IntPtr hwnd, GetAncestorFlags flags);

	[DllImport("kernel32.dll")]
	static extern IntPtr GetConsoleWindow();

	IntPtr GetConsoleOrTerminalWindow()
	{
		IntPtr consoleHandle = GetConsoleWindow();
		IntPtr handle = GetAncestor(consoleHandle, GetAncestorFlags.GetRootOwner);
		return handle;
	}
}

/*
	[1]
		The code is based on:
			https://learn.microsoft.com/en-us/entra/msal/dotnet/acquiring-tokens/desktop-mobile/wam
		also note:
			- removed .WithDefaultRedirectUri() because of:
				https://learn.microsoft.com/en-us/entra/msal/dotnet/acquiring-tokens/desktop-mobile/wam#:~:text=WAM%20redirect%20URIs%20do%20not%20need%20to%20be%20configured%20in%20MSAL
			- added .WithAuthority() since I use "Personal Accounts only" in the app registration on azure.
				https://learn.microsoft.com/en-us/dotnet/api/microsoft.identity.client.abstractapplicationbuilder-1.withauthority?view=msal-dotnet-latest#microsoft-identity-client-abstractapplicationbuilder-1-withauthority(microsoft-identity-client-aadauthorityaudience-system-boolean)
			- Build() might throw a MsalClientException:
				https://learn.microsoft.com/en-us/dotnet/api/microsoft.identity.client.publicclientapplicationbuilder.build?view=msal-dotnet-latest#microsoft-identity-client-publicclientapplicationbuilder-build
			  and the possible causes are not so clear.
			- added an explicit cast to PublicClientApplication, which is the only concrete class in the hierarchy:
				https://learn.microsoft.com/en-us/dotnet/api/microsoft.identity.client.publicclientapplication?view=msal-dotnet-latest
			  in order to use GetAccountAsync(CancellationToken) (which is introduced in ClientApplicationBase)
			  rather than GetAccountAsync() (which is introduced in IPublicClientApplication).

	[2]
		I use a static factory method because of MsalCacheHelper.CreateAsync(), which is asynchronous, and the
		absence of a synchronous version. I use a constructor so that _app can be declared readonly.

	[3]
		The following is based on:
			https://learn.microsoft.com/en-us/entra/msal/dotnet/how-to/token-cache-serialization?tabs=desktop

	[4]
		This and the following are based on:
			https://learn.microsoft.com/en-us/entra/msal/dotnet/acquiring-tokens/desktop-mobile/wam

	[5]
		Check:
			https://learn.microsoft.com/en-us/onedrive/developer/rest-api/concepts/permissions_reference?view=odsp-graph-online
		and note that Azure complains if User.Read is removed.

	[6]
		I declared _app as a ClientApplicationBase, instead of IPublicClientApplication as the tutorial suggests,
		in order to use the RunAsync(CancellationToken) method (since the latter only has RunAsync()).

	[7]
		Check:
			https://learn.microsoft.com/en-us/entra/msal/dotnet/advanced/exceptions/

	[9]
		The following introduces the GetConsoleOrTerminalWindow() method. Based on:
			https://learn.microsoft.com/en-us/entra/msal/dotnet/acquiring-tokens/desktop-mobile/wam#:~:text=For%20console%20applications

	[10]
		The user refused to authenticate.
	
	[11]
		I catch "exception" because this:
			https://learn.microsoft.com/en-us/entra/msal/dotnet/advanced/exceptions/#:~:text=No%20other%20exception%20is%20caught%20by%20MSAL.%20Any%20network%20issues%2C%20cancellations%20etc.%20are%20bubbled%20up%20to%20the%20application.
		seems not very precise.
*/