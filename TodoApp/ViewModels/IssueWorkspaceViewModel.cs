using System.Collections.ObjectModel;

namespace TodoApp;

internal sealed class IssueWorkspaceViewModel
{
    public ObservableCollection<IssueItem> Issues { get; } = new();

    public ObservableCollection<IssueItem> VisibleIssues { get; } = new();

    public ObservableCollection<IssueItem> BacklogIssues { get; } = new();

    public ObservableCollection<IssueItem> TodoIssues { get; } = new();

    public ObservableCollection<IssueItem> InProgressIssues { get; } = new();

    public ObservableCollection<IssueItem> ReviewIssues { get; } = new();

    public ObservableCollection<IssueItem> DoneIssues { get; } = new();

    public string NavigationScope { get; set; } = "All";

    public string SearchQuery { get; set; } = string.Empty;

    public string StatusFilter { get; set; } = "All";

    public string PriorityFilter { get; set; } = "All";

    public string ScopeLabel => NavigationScope switch
    {
        "Mine" => AppResources.Get("ScopeMine"),
        "Release" => AppResources.Get("ScopeRelease"),
        "Done" => AppResources.Get("ScopeDone"),
        _ => AppResources.Get("ScopeAll")
    };

    public IReadOnlyList<IssueItem> Refresh()
    {
        var filtered = Issues.Where(MatchesCurrentScope).Where(MatchesFilters).ToList();
        ReplaceItems(VisibleIssues, filtered);
        ReplaceItems(BacklogIssues, filtered.Where(issue => issue.Status == "Backlog"));
        ReplaceItems(TodoIssues, filtered.Where(issue => issue.Status == "Todo"));
        ReplaceItems(InProgressIssues, filtered.Where(issue => issue.Status == "InProgress"));
        ReplaceItems(ReviewIssues, filtered.Where(issue => issue.Status == "Review"));
        ReplaceItems(DoneIssues, filtered.Where(issue => issue.Status == "Done"));
        return filtered;
    }

    public string NextIssueKey()
    {
        var max = Issues
            .Select(issue => issue.Key)
            .Select(key => key.StartsWith("TD-", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(key[3..], out var value)
                    ? value
                    : 100)
            .DefaultIfEmpty(100)
            .Max();

        return $"TD-{max + 1}";
    }

    private bool MatchesCurrentScope(IssueItem issue)
    {
        return NavigationScope switch
        {
            "Mine" => issue.Assignee == "Me",
            "Release" => issue.Labels.Contains("release", StringComparison.OrdinalIgnoreCase)
                || issue.Project == "Ops",
            "Done" => issue.Status == "Done",
            _ => true
        };
    }

    private bool MatchesFilters(IssueItem issue)
    {
        var matchesQuery = string.IsNullOrWhiteSpace(SearchQuery)
            || issue.Title.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase)
            || issue.Description.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase)
            || issue.Assignee.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase)
            || issue.Project.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase)
            || issue.Labels.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase)
            || issue.Key.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase);

        var matchesStatus = StatusFilter == "All" || issue.Status == StatusFilter;
        var matchesPriority = PriorityFilter == "All" || issue.Priority == PriorityFilter;
        return matchesQuery && matchesStatus && matchesPriority;
    }

    private static void ReplaceItems(ObservableCollection<IssueItem> target, IEnumerable<IssueItem> source)
    {
        target.Clear();
        foreach (var item in source)
        {
            target.Add(item);
        }
    }
}
