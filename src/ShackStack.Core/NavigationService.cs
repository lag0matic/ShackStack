using ShackStack.Core.Abstractions.Contracts;
using ShackStack.Core.Abstractions.Models;

namespace ShackStack.Core;

public sealed class NavigationService(SessionStateStore stateStore) : INavigationService
{
    public WorkspaceKind Current => stateStore.CurrentWorkspace;

    public void NavigateTo(WorkspaceKind workspace)
    {
        stateStore.SetWorkspace(workspace);
    }
}
