using MySync.Utilities;

namespace MySync.Monitoring;

internal class RulesManager
{
	HashSet<string> _whitelist = null!;
	HashSet<string> _blacklist = null!;

	RulesManager()
	{
	}

    internal static async Task<RulesManager?> TryCreateAsync()
    {
        var ruleManager = new RulesManager();
		return await ruleManager.TryReloadAsync() ? ruleManager : null;
    }

    internal async Task<bool> TryReloadAsync()
    {
        var content = await FileUtils.TryReadFileAsync(FileUtils.RulesPath);
		if (content is null)
			return false;
		var lines = content.Split(['\r','\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		HashSet<string> whitelist = new(), blacklist = new();
		foreach (var line in lines)
		{
    		if (!line.StartsWith("not ", StringComparison.Ordinal))
				whitelist.Add(line);
			else
				blacklist.Add(line[4..].TrimStart());
		}
		_whitelist = whitelist;
		_blacklist = blacklist;
		return true;
    }

	internal bool IsAllowed(string relPath)
		=> IsPathInSet(relPath, _whitelist) && !IsPathInSet(relPath, _blacklist);

	static bool IsPathInSet(string? relPath, HashSet<string> set)
	{
		while (!string.IsNullOrEmpty(relPath))
		{
			if (set.Contains(relPath))
				return true;
			relPath = Path.GetDirectoryName(relPath);
		}
		return false;
	}
}