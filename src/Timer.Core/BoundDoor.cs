namespace Stagewatch.Core;

/// <summary>
/// A server's door as it bound it, published whole once its listener runs (null while it does not): its port, and whether it
/// took every interface (false: this machine only). One immutable value, so a reader on another thread never pairs one start's
/// port with another start's bind — what a hello names (Show Bus §2) is a door that was really opened.
/// </summary>
public sealed record BoundDoor(int Port, bool BindAll);
