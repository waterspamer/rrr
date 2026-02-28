public class GameStateMachine
{
    public IGameState CurrentState { get; private set; }

    public void ChangeState(IGameState nextState)
    {
        if (ReferenceEquals(CurrentState, nextState))
            return;

        CurrentState?.Exit();
        CurrentState = nextState;
        CurrentState?.Enter();
    }

    public void Tick()
    {
        CurrentState?.Tick();
    }
}
