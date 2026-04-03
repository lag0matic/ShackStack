using ShackStack.Core.Abstractions.Models;

namespace ShackStack.Core.Abstractions.Contracts;

public interface INavigationService
{
    WorkspaceKind Current { get; }
    void NavigateTo(WorkspaceKind workspace);
}
