using GameServer.Movement;
using Xunit;

namespace GameServer.Tests;

public class MovementValidatorTests
{
    private static readonly MovementConfig Config = MovementConfig.Default;

    [Fact]
    public void ValidatePosition_NanX_ReturnsError()
    {
        var reason = MovementValidator.ValidatePosition(double.NaN, 4.0, 0.5, 0.5, 4.0, 0.5, Config);
        Assert.NotNull(reason);
        Assert.Contains("NaN", reason);
    }

    [Fact]
    public void ValidatePosition_InfinityY_ReturnsError()
    {
        var reason = MovementValidator.ValidatePosition(0.5, double.PositiveInfinity, 0.5, 0.5, 4.0, 0.5, Config);
        Assert.NotNull(reason);
        Assert.Contains("Infinity", reason);
    }

    [Fact]
    public void ValidatePosition_YBelowMin_ReturnsError()
    {
        var reason = MovementValidator.ValidatePosition(0.5, Config.MinY - 1, 0.5, 0.5, 4.0, 0.5, Config);
        Assert.NotNull(reason);
        Assert.Contains("out of bounds", reason);
    }

    [Fact]
    public void ValidatePosition_YAboveMax_ReturnsError()
    {
        var reason = MovementValidator.ValidatePosition(0.5, Config.MaxY + 1, 0.5, 0.5, 4.0, 0.5, Config);
        Assert.NotNull(reason);
        Assert.Contains("out of bounds", reason);
    }

    [Fact]
    public void ValidatePosition_ExcessiveDistance_ReturnsError()
    {
        var reason = MovementValidator.ValidatePosition(
            0.5 + Config.MaxMoveDistancePerTick + 10, 4.0, 0.5,
            0.5, 4.0, 0.5, Config);
        Assert.NotNull(reason);
        Assert.Contains("too large", reason);
    }

    [Fact]
    public void ValidatePosition_VerticalSpeedAnomaly_ReturnsError()
    {
        var reason = MovementValidator.ValidatePosition(0.5, 4.0 + Config.MaxVerticalSpeed + 10, 0.5, 0.5, 4.0, 0.5, Config);
        Assert.NotNull(reason);
        Assert.Contains("Vertical speed anomaly", reason);
    }

    [Fact]
    public void ValidatePosition_HorizontalSpeedAnomaly_ReturnsError()
    {
        var reason = MovementValidator.ValidatePosition(
            0.5 + Config.MaxHorizontalSpeedPerTick + 5, 4.0, 0.5,
            0.5, 4.0, 0.5, Config);
        Assert.NotNull(reason);
        Assert.Contains("Horizontal speed anomaly", reason);
    }

    [Fact]
    public void ValidatePosition_LegalMove_ReturnsNull()
    {
        var reason = MovementValidator.ValidatePosition(1.5, 4.5, 1.5, 0.5, 4.0, 0.5, Config);
        Assert.Null(reason);
    }

    [Fact]
    public void ValidateLook_NanYaw_ReturnsError()
    {
        var reason = MovementValidator.ValidateLook(float.NaN, 0f);
        Assert.NotNull(reason);
        Assert.Contains("Yaw", reason);
    }

    [Fact]
    public void ValidateLook_NanPitch_ReturnsError()
    {
        var reason = MovementValidator.ValidateLook(0f, float.NaN);
        Assert.NotNull(reason);
        Assert.Contains("Pitch", reason);
    }

    [Fact]
    public void ValidateLook_Legal_ReturnsNull()
    {
        var reason = MovementValidator.ValidateLook(90f, 0f);
        Assert.Null(reason);
    }

    [Fact]
    public void ValidatePositionAndLook_DelegatesToBoth()
    {
        var reason = MovementValidator.ValidatePositionAndLook(
            0.5, Config.MaxY + 1, 0.5, float.NaN, 0f,
            0.5, 4.0, 0.5, 0f, 0f, Config);
        Assert.NotNull(reason);
        Assert.Contains("out of bounds", reason);
    }
}
