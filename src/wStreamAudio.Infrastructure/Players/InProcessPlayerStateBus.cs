using wStreamAudio.Core.Abstractions;

namespace wStreamAudio.Infrastructure.Players;

public sealed class InProcessPlayerStateBus : IPlayerStateBus
{
    public event EventHandler<PlayerChangedEventArgs>? PlayerChanged;

    public void RaisePlayerChanged(PlayerChangedEventArgs args)
        => PlayerChanged?.Invoke(this, args);
}
