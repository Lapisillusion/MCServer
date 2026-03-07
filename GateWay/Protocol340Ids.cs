// Protocol340Ids.cs

namespace GateWay;

public static class Protocol340Ids
{
    // 这些是“客户端->服务端”的 packetId（在对应状态下）
    // 你们内部已有映射表的话，直接替换这里。
    public const int C2S_Handshake = 0x00; // Handshaking state
    public const int C2S_StatusRequest = 0x00; // Status state
    public const int C2S_StatusPing = 0x01; // Status state
    public const int C2S_LoginStart = 0x00; // Login state
    public const int S2C_LoginSuccess = 0x02; // Login state
    public const int S2C_StatusResponse = 0x00; // Status state
    public const int S2C_StatusPong = 0x01; // Status state

    // 可选：如果你要在网关直接返回断开原因（MVP 可不做）
    // public const int S2C_LoginDisconnect = ...
}
