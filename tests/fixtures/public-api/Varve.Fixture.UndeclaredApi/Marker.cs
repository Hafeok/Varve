namespace Varve.Fixture.UndeclaredApi;

/// <summary>
/// A public type that appears in neither PublicAPI.Shipped.txt nor
/// PublicAPI.Unshipped.txt, both of which sit beside this project and contain
/// only their header. This must not compile.
/// </summary>
public sealed class Marker
{
    /// <summary>A public member nobody declared.</summary>
    public int Value { get; }
}
