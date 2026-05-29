namespace transdb_geocoding.Services;

public enum AppReadinessState
{
    Starting,
    Importing,
    Ready,
    Failed,
}

/// <summary>
/// Singleton that tracks the application's startup / import readiness state.
/// Written to by <see cref="DataImportHostedService"/>;
/// read by the /health endpoint and any other component that needs to gate on readiness.
/// </summary>
public class ReadinessService
{
    private volatile AppReadinessState _state = AppReadinessState.Starting;

    public AppReadinessState State => _state;
    public bool IsReady => _state == AppReadinessState.Ready;

    public void SetImporting() => _state = AppReadinessState.Importing;
    public void SetReady() => _state = AppReadinessState.Ready;
    public void SetFailed() => _state = AppReadinessState.Failed;
}
