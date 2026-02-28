using UnityEngine;

public class GameState : IGameState
{
    private readonly GameFlowController flow;

    public GameState(GameFlowController flow)
    {
        this.flow = flow;
    }

    public void Enter() => flow.StartGameplay();
    public void Exit() => flow.StopGameplay();

    public void Tick()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
            flow.OpenMenu();
    }
}
