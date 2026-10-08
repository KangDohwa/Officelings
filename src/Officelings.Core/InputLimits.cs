namespace Officelings.Core;

internal static class InputLimits
{
    internal const int Agents = 2048, Sources = 256, AgentsPerSource = 128;
    internal const int PollBytes = 16 * 1024 * 1024, Records = 16384;
    internal static bool ConversationId(string? id) => id is { Length: > 0 and <= 128 } &&
        id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}
