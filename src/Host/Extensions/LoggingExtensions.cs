using Microsoft.Extensions.Logging;

namespace Snail.MCP.Plans.Host.Extensions;

/// <summary>Logging setup for a stdio MCP server.</summary>
public static class LoggingExtensions
{
    extension(ILoggingBuilder logging)
    {
        /// <summary>
        /// Sends every log level to stderr: stdout belongs to the JSON-RPC transport and a single stray
        /// line there would corrupt the protocol.
        /// </summary>
        public ILoggingBuilder AddMcpStdioLogging()
        {
            logging.AddConsole(options =>
            {
                options.LogToStandardErrorThreshold = LogLevel.Trace;
            });

            return logging;
        }
    }
}
