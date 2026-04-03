using ShackStack.Core.Abstractions.Models;

namespace ShackStack.Core;

public sealed class SessionStateStore
{
    public WorkspaceKind CurrentWorkspace { get; private set; } = WorkspaceKind.Operating;

    public void SetWorkspace(WorkspaceKind workspace)
    {
        CurrentWorkspace = workspace;
    }
}
