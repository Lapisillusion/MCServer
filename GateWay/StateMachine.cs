using Common.MC;

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
            ConnState.Handshake => packetId == Protocol340Ids.Handshake.C2S_Handshake,
            ConnState.Status => packetId == Protocol340Ids.Status.C2S_Request
                                || packetId == Protocol340Ids.Status.C2S_Ping,
            ConnState.Login => packetId == Protocol340Ids.Login.C2S_LoginStart,
            ConnState.Play => true, // MVP: 不拦 Play
            _ => false
        };
    }
}