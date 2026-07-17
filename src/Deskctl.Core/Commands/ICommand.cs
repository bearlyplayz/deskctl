namespace Deskctl.Core.Commands;

/// <summary>
/// A capability, expressed once. The CLI and the MCP server are both thin adapters over
/// implementations of this interface, which is what keeps the two surfaces from drifting
/// apart.
/// </summary>
public interface ICommand<in TIn, TOut>
{
    Task<TOut> RunAsync(TIn input, CancellationToken ct);
}
