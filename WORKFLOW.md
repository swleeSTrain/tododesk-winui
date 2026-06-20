---
tracker:
  kind: linear
  api_key: $LINEAR_API_KEY
  project_slug: $SYMPHONY_LINEAR_PROJECT_SLUG
  required_labels:
    - symphony
  active_states:
    - Todo
    - In Progress
    - Rework
  terminal_states:
    - Closed
    - Cancelled
    - Canceled
    - Duplicate
    - Done
polling:
  interval_ms: 30000
workspace:
  root: $SYMPHONY_WORKSPACE_ROOT
hooks:
  after_create: |
    pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/symphony/after-create.ps1
  before_run: |
    pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/symphony/before-run.ps1
agent:
  max_concurrent_agents: 2
  max_turns: 12
  max_retry_backoff_ms: 300000
codex:
  command: codex app-server
  thread_sandbox: workspace-write
  turn_sandbox_policy:
    type: workspaceWrite
    networkAccess: true
---
You are working on TodoDesk from a Linear issue.

Issue context:
- Identifier: `{{ issue.identifier }}`
- Title: {{ issue.title }}
- Current state: {{ issue.state }}
- Labels: {{ issue.labels }}
- URL: {{ issue.url }}

{% if attempt %}
Continuation context:
- This is retry/continuation attempt #{{ attempt }}.
- Resume from the current workspace state. Do not repeat completed investigation unless the previous result is stale or contradicted by new evidence.
{% endif %}

Description:
{% if issue.description %}
{{ issue.description }}
{% else %}
No description was provided. Infer only the smallest safe scope from the title and record assumptions.
{% endif %}

## Operating Contract

- Work only inside the Symphony workspace for this issue.
- Treat this repository as a WinUI 3 desktop application named TodoDesk.
- Keep changes scoped to the Linear issue. Do not bundle unrelated visual refactors, package metadata churn, or broad cleanup.
- Preserve user or teammate edits already present in the worktree.
- Use the existing single-page app structure unless the task clearly needs a new module.
- Prefer native WinUI controls and theme-aware resources for UI changes.
- Keep both the Fluent native surface and the custom theme surface coherent when changing shared issue workflows.
- If a task touches app behavior or XAML, validate with `dotnet build .\TodoApp\TodoApp.csproj -p:Platform=x64`.
- If the change is UI-facing and the machine can launch packaged WinUI apps, run the app and capture a brief validation note describing the path tested.

## Repo Map

- `TodoApp/MainPage.xaml`: issue board UI, Fluent surface, custom theme surface, detail editor.
- `TodoApp/MainPage.xaml.cs`: local JSON persistence, filtering, selection, issue model, and UI event handlers.
- `TodoApp/MainWindow.xaml(.cs)`: shell, title bar, window treatment, theme handling.
- `TodoApp/VisualThemeManager.cs`: theme definitions and resource palette switching.
- `ui-tests.ps1`: WinApp-driven smoke tests for core UI flows.

## Expected Flow

1. Inspect the current issue, repository state, and relevant files.
2. Write a short plan before editing.
3. Implement the smallest complete change.
4. Run targeted validation. Prefer the existing project build command for code changes:

   ```powershell
   dotnet build .\TodoApp\TodoApp.csproj -p:Platform=x64
   ```

5. Summarize changed files, validation results, and any blocker that prevented full verification.

## Handoff Bar

Before considering the issue ready for review:

- The issue request is implemented or a true external blocker is documented.
- Build/test evidence is recorded.
- UI-facing changes include a short manual validation path or an explicit reason launch validation could not run.
- No unrelated worktree changes were reverted or swept into the implementation.
