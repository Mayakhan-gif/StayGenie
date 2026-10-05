using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;

namespace StayGenie.Services;

// Ye class chatbot ki taraf se MCP server (/mcp) se baat karti hai
// aur uske auzaar (tools) laa kar deti hai
public class McpToolProvider
{
    private McpClient? _client;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task<IList<AITool>> GetToolsAsync()
    {
        if (_client is null)
        {
            await _lock.WaitAsync();
            try
            {
                if (_client is null)
                {
                    var transport = new HttpClientTransport(new HttpClientTransportOptions
                    {
                        Name = "StayGenie MCP",
                        Endpoint = new Uri("https://localhost:7115/mcp")
                    });
                    _client = await McpClient.CreateAsync(transport);
                }
            }
            finally
            {
                _lock.Release();
            }
        }

        var tools = await _client.ListToolsAsync();
        return tools.Cast<AITool>().ToList();
    }
}