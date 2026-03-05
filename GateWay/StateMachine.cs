// StateMachine.cs

namespace GateWay;

public enum ConnState : byte
{
    Handshake,
    Status,
    Login,
    Play
}

public static class StateMachine
{
    // MVP：只严格校验 Handshake/Status/Login；Play 先全放行
    public static bool IsAllowed(ConnState state, int packetId)
    {
        return state switch
        {
            ConnState.Handshake => packetId == Protocol340Ids.C2S_Handshake,
            ConnState.Status => packetId == Protocol340Ids.C2S_StatusRequest
                                || packetId == Protocol340Ids.C2S_StatusPing,
            ConnState.Login => packetId == Protocol340Ids.C2S_LoginStart,
            ConnState.Play => true, // MVP: 不拦 Play
            _ => false
        };
    }
}