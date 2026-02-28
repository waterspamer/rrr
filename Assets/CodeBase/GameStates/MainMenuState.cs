public class MainMenuState : IGameState
{
    private readonly GameFlowController flow;

    public MainMenuState(GameFlowController flow)
    {
        this.flow = flow;
    }

    public void Enter() => flow.ShowMainMenu();
    public void Exit() => flow.HideMainMenu();
    public void Tick() { }
}
