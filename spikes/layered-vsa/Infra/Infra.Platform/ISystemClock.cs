namespace Infra.Platform;

public interface ISystemClock
{
    DateTimeOffset Now { get; }
}
