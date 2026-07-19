using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using WinDeskCtl.Core.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;

namespace WinDeskCtl.Mcp;

internal static class McpHost
{
    internal static async Task<int> RunAsync(CancellationToken ct)
    {
        HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());

        // stdout carries the MCP protocol. Anything else written there corrupts the stream,
        // so every log goes to stderr.
        builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

        // Tools are registered explicitly rather than via WithToolsFromAssembly: assembly
        // scanning is reflection, which NativeAOT cannot do. Passing the source-gen
        // options here is what makes tool inputs and results serialize without dynamic code.
        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithTools<WinDeskCtlTools>(ToolSerializerOptions());

        await builder.Build().RunAsync(ct);
        return 0;
    }

    /// <summary>
    /// windeskctl's own DTO metadata, plus the SDK's for the protocol types a tool may return.
    /// </summary>
    /// <remarks>
    /// Two resolvers because the ownership is genuinely split: WinDeskCtlJsonContext cannot describe
    /// CallToolResult — Core does not reference the MCP SDK — and the SDK's context does not know
    /// windeskctl's DTOs. Under NativeAOT a type neither resolver covers is not a slow path: the SDK
    /// builds every tool's schema at startup, so one missing type takes down the whole server
    /// before a single request is served, and only on the published binary, since JIT's
    /// reflection fallback hides it.
    /// </remarks>
    private static JsonSerializerOptions ToolSerializerOptions()
    {
        JsonSerializerOptions options = new(WinDeskCtlJson.Options)
        {
            TypeInfoResolver = JsonTypeInfoResolver.Combine(
                WinDeskCtlJson.Options.TypeInfoResolver,
                McpJsonUtilities.DefaultOptions.TypeInfoResolver),
        };
        options.MakeReadOnly();
        return options;
    }
}
