namespace GameServer.Core.Dispatch;

public enum GameSessionState : byte
{
    New = 0,
    Joining = 1,
    Play = 2,
    Closing = 3
}
