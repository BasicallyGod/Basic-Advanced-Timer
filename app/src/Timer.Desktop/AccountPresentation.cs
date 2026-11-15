using Timer.Core;

namespace Timer.Desktop;

/// <summary>Presentation data only: Key remains the existing account association, never a formatted label.</summary>
public sealed record AccountPresentation(string Key, string DisplayName, string Colour, bool Hidden, bool Historical, int? Pattern = null)
{
    public static string Label(string key) => BreakAccount.Display(key);
    public static string BudgetDescription(TimerModel model, TimerAccount account, TimeSpan? used = null, DateTime? reference = null)
    {
        if (account.Budget is null) return "No budget";
        var at = reference ?? model.Engine.Now; var value = used ?? model.BudgetUsedAt(account, at);
        return $"{model.BudgetUsage(account, value)} · {model.BudgetPeriodNameAt(account, at)} · {model.BudgetDetail(account, value)}";
    }
}

/// <summary>One catalogue read per refresh; ordinary labels do not allocate E-ink patterns or read budget totals.</summary>
public sealed class AccountCatalogue
{
    private readonly Dictionary<string, AccountPresentation> byKey;
    private readonly Dictionary<string, Timer.Storage.AccountSettings> configured;
    public IReadOnlyList<AccountPresentation> Items { get; }
    public AccountPresentation Find(string key) => byKey.TryGetValue(key, out var account) ? account
        : configured.TryGetValue(key, out var saved) ? new(saved.Name, AccountPresentation.Label(saved.Name), saved.Colour ?? "#91B7D5", saved.Hidden, false)
        : new(key, AccountPresentation.Label(key), "#91B7D5", false, true);
    private AccountCatalogue(IReadOnlyList<AccountPresentation> items, Dictionary<string, Timer.Storage.AccountSettings> configured) { Items = items; this.configured = configured; byKey = items.ToDictionary(a => a.Key, StringComparer.OrdinalIgnoreCase); }
    public static AccountCatalogue Capture(TimerModel model, IReadOnlyList<TimerAccount>? accounts = null, bool patterns = false)
    {
        var configured = model.AccountSettings.ToDictionary(a => a.Name, StringComparer.OrdinalIgnoreCase);
        var items = (accounts ?? model.ReviewAccounts).Select(a => new AccountPresentation(a.Name, a.Display, a.Colour,
            configured.TryGetValue(a.Name, out var saved) && saved.Hidden, !configured.ContainsKey(a.Name) && !BreakAccount.Is(a.Name),
            patterns && !BreakAccount.Is(a.Name) ? model.TextureOf(a.Name) : null)).ToArray();
        return new(items, configured);
    }
}
