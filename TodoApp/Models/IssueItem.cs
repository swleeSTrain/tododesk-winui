using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace TodoApp;

public sealed class IssueItem : INotifyPropertyChanged
{
    private string _title;
    private string _description;
    private string _status;
    private string _priority;
    private string _assignee;
    private string _project;
    private string _dueDate;
    private string _labels;
    private bool _isSelected;

    public IssueItem(
        string id,
        string key,
        string title,
        string description,
        string status,
        string priority,
        string assignee,
        string project,
        string dueDate,
        string labels)
    {
        Id = id;
        Key = key;
        _title = title;
        _description = description;
        _status = status;
        _priority = priority;
        _assignee = assignee;
        _project = project;
        _dueDate = dueDate;
        _labels = labels;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id { get; }

    public string Key { get; }

    public string Title
    {
        get => _title;
        set
        {
            if (SetField(ref _title, value))
            {
                OnPropertyChanged(nameof(AutomationName));
            }
        }
    }

    public string Description
    {
        get => _description;
        set => SetField(ref _description, value);
    }

    public string Status
    {
        get => _status;
        set
        {
            if (SetField(ref _status, value))
            {
                OnPropertyChanged(nameof(StatusLabel));
                OnPropertyChanged(nameof(Metadata));
                OnPropertyChanged(nameof(AutomationName));
            }
        }
    }

    public string Priority
    {
        get => _priority;
        set
        {
            if (SetField(ref _priority, value))
            {
                OnPropertyChanged(nameof(PriorityLabel));
                OnPropertyChanged(nameof(PriorityToken));
                OnPropertyChanged(nameof(RowSubtitle));
                OnPropertyChanged(nameof(Metadata));
            }
        }
    }

    public string Assignee
    {
        get => _assignee;
        set
        {
            if (SetField(ref _assignee, value))
            {
                OnPropertyChanged(nameof(Metadata));
                OnPropertyChanged(nameof(AutomationName));
                OnPropertyChanged(nameof(AssigneeDisplay));
            }
        }
    }

    public string Project
    {
        get => _project;
        set
        {
            if (SetField(ref _project, value))
            {
                OnPropertyChanged(nameof(RowSubtitle));
                OnPropertyChanged(nameof(Metadata));
            }
        }
    }

    public string DueDate
    {
        get => _dueDate;
        set
        {
            if (SetField(ref _dueDate, value))
            {
                OnPropertyChanged(nameof(DueDateDisplay));
                OnPropertyChanged(nameof(Metadata));
            }
        }
    }

    public string DueDateDisplay
    {
        get
        {
            return DateTimeOffset.TryParseExact(
                DueDate,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date)
                ? date.ToString("d", CultureInfo.CurrentCulture)
                : DueDate;
        }
    }

    public string Labels
    {
        get => _labels;
        set
        {
            if (SetField(ref _labels, value))
            {
                OnPropertyChanged(nameof(RowSubtitle));
                OnPropertyChanged(nameof(Metadata));
            }
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectionFillOpacity));
            OnPropertyChanged(nameof(SelectionRingOpacity));
        }
    }

    public string StatusLabel => Status switch
    {
        "Backlog" => AppResources.Get("StatusBacklog"),
        "Todo" => AppResources.Get("StatusTodo"),
        "InProgress" => AppResources.Get("StatusInProgress"),
        "Review" => AppResources.Get("StatusReview"),
        "Done" => AppResources.Get("StatusDone"),
        _ => Status
    };

    public string PriorityLabel => Priority switch
    {
        "Urgent" => AppResources.Get("PriorityUrgent"),
        "High" => AppResources.Get("PriorityHigh"),
        "Medium" => AppResources.Get("PriorityMedium"),
        "Low" => AppResources.Get("PriorityLow"),
        _ => Priority
    };

    public string AssigneeDisplay => Assignee == "Me" ? AppResources.Get("AssigneeMe") : Assignee;

    public string Metadata => $"{Project} · {AssigneeDisplay} · {Labels}";

    public string PriorityToken => Priority switch
    {
        "Urgent" => "P0",
        "High" => "P1",
        "Medium" => "P2",
        "Low" => "P3",
        _ => "P?"
    };

    public string RowSubtitle => $"{PriorityToken} · {Project} · {Labels}";

    public string AutomationName => $"{Key} {Title} {StatusLabel} {AssigneeDisplay}";

    public double SelectionFillOpacity => IsSelected ? 0.10 : 0;

    public double SelectionRingOpacity => IsSelected ? 0.30 : 0;

    public static IssueItem Create(string key, string title)
    {
        return new IssueItem(
            Guid.NewGuid().ToString("N"),
            key,
            title,
            AppResources.Get("NewIssueDescription"),
            "Todo",
            "Medium",
            "Me",
            "Platform",
            string.Empty,
            "triage");
    }

    public static IssueItem FromSnapshot(IssueSnapshot snapshot)
    {
        return new IssueItem(
            string.IsNullOrWhiteSpace(snapshot.Id) ? Guid.NewGuid().ToString("N") : snapshot.Id,
            string.IsNullOrWhiteSpace(snapshot.Key) ? "TD-100" : snapshot.Key,
            string.IsNullOrWhiteSpace(snapshot.Title) ? AppResources.Get("UntitledIssue") : snapshot.Title,
            snapshot.Description ?? string.Empty,
            string.IsNullOrWhiteSpace(snapshot.Status) ? "Todo" : snapshot.Status,
            string.IsNullOrWhiteSpace(snapshot.Priority) ? "Medium" : snapshot.Priority,
            string.IsNullOrWhiteSpace(snapshot.Assignee) || snapshot.Assignee == "나" ? "Me" : snapshot.Assignee,
            string.IsNullOrWhiteSpace(snapshot.Project) ? "Platform" : snapshot.Project,
            snapshot.DueDate ?? string.Empty,
            snapshot.Labels ?? string.Empty);
    }

    private bool SetField(ref string field, string value, [CallerMemberName] string? propertyName = null)
    {
        value ??= string.Empty;
        if (field == value)
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed record IssueSnapshot(
    string Id,
    string Key,
    string Title,
    string Description,
    string Status,
    string Priority,
    string Assignee,
    string Project,
    string DueDate,
    string Labels)
{
    public static IssueSnapshot FromItem(IssueItem item)
    {
        return new IssueSnapshot(
            item.Id,
            item.Key,
            item.Title,
            item.Description,
            item.Status,
            item.Priority,
            item.Assignee,
            item.Project,
            item.DueDate,
            item.Labels);
    }
}

public sealed record LegacyTodoSnapshot(string Id, string Title, bool IsCompleted);
