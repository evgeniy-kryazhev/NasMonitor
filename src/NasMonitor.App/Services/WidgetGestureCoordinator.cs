namespace NasMonitor.App.Services;

/// <summary>Serializes desktop pointer gestures across widget windows on the UI thread.</summary>
public sealed class WidgetGestureCoordinator
{
    private object? owner;

    /// <summary>Claim the current gesture unless another window already owns it.</summary>
    public bool TryAcquire(object candidate)
    {
        if (owner is not null && !ReferenceEquals(owner, candidate))
        {
            return false;
        }

        owner = candidate;
        return true;
    }

    /// <summary>Release only the gesture owned by the specified window.</summary>
    public void Release(object candidate)
    {
        if (ReferenceEquals(owner, candidate))
        {
            owner = null;
        }
    }
}
