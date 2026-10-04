using ModelContextProtocol;

namespace HicasTest.Mcp;

/// <summary>
/// The SDK replaces the message of any exception other than <see cref="McpException"/> with a generic
/// "An error occurred invoking …". Tools that drive a host need the real reason (timeout, no such control, …).
/// </summary>
internal static class ToolErrors
{
    public static async Task<string> Surface(Func<Task<string>> body)
    {
        try
        {
            return await body();
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not McpException)
        {
            throw new McpException(ex.Message, ex);
        }
    }

    public static string Surface(Func<string> body)
    {
        try
        {
            return body();
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not McpException)
        {
            throw new McpException(ex.Message, ex);
        }
    }
}
