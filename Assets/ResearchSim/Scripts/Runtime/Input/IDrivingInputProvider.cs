namespace ResearchSim
{
    public interface IDrivingInputProvider
    {
        string ProviderName { get; }
        bool IsAvailable { get; }
        bool TryGetInput(out DrivingInputState state);
    }
}
