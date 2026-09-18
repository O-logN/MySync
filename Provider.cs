using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Broker;
using Microsoft.Identity.Client.Extensions.Msal;

class Provider
{
	const string ClientId = ; // Ive purposely removed it
	readonly PublicClientApplication _app;
	
	Provider(string clientId)
	{
		_app = (PublicClientApplication)
				PublicClientApplicationBuilder.Create(clientId) // [1]
				.WithParentActivityOrWindow(GetConsoleOrTerminalWindow)
				.WithAuthority(AadAuthorityAudience.PersonalMicrosoftAccount)
				.WithBroker(new BrokerOptions(BrokerOptions.OperatingSystems.Windows))
				.Build();
	}
	
	internal static async Task<Provider> CreateAsync() // [2]
	{
		var provider = new Provider(ClientId); // [3]
		var tmp = await MsalCacheHelper.CreateAsync(
			new StorageCreationPropertiesBuilder(
				FileManager.GetCacheFileName(),
				FileManager.GetMySyncPath()
			).Build()
		);
		tmp.RegisterCache(provider._app.UserTokenCache);
		return provider;
	}

	internal async Task<string> GetAccessTokenAsync(CancellationToken cToken)
	{
		string[] scopes = ["User.Read", "Files.ReadWrite"]; // [4] , [5]
		IAccount? account = (await _app.GetAccountsAsync(cToken)).FirstOrDefault(); // [6]
		if (account is not null)
		{
			try
			{
				return (await _app.AcquireTokenSilent(scopes, account).ExecuteAsync(cToken)).AccessToken;
			}
			catch (MsalUiRequiredException) {} // [7]
		}
		var ans = (await _app.AcquireTokenInteractive(scopes)
							.WithPrompt(Prompt.SelectAccount)
							.ExecuteAsync(cToken)).AccessToken;

		return (await _app.GetAccountsAsync(cToken)).Count() > 1 ? // [8]
				throw new InvalidOperationException("Another account is already in cache.") : ans;
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

	[8]
		This is to enforce the assumption made in [0] of Program.cs.

	[9]
		The following introduces the GetConsoleOrTerminalWindow() method. Based on:
			https://learn.microsoft.com/en-us/entra/msal/dotnet/acquiring-tokens/desktop-mobile/wam#:~:text=For%20console%20applications
*/