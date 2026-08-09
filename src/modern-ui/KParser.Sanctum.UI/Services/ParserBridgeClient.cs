using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using KParser.Sanctum.UI.Bridge;

namespace KParser.Sanctum.UI.Services;

internal sealed class ParserBridgeClient
{
    private const string PipeName = "KParser.Sanctum.Modern.v1";
    private const int MaxResponseCharacters = 4 * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<BridgeSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        return await GetSnapshotAsync(
            "all",
            0,
            null,
            "damageDealt",
            "all",
            "summary",
            "player",
            null,
            false,
            cancellationToken);
    }

    public async Task<BridgeSnapshot> GetSnapshotAsync(
        string scope,
        int battleId,
        string? mobName,
        string report,
        string combatantScope,
        string displayMode,
        string groupMode,
        CancellationToken cancellationToken)
    {
        return await GetSnapshotAsync(
            scope,
            battleId,
            mobName,
            report,
            combatantScope,
            displayMode,
            groupMode,
            null,
            false,
            cancellationToken);
    }

    public async Task<BridgeSnapshot> GetSnapshotAsync(
        string scope,
        int battleId,
        string? mobName,
        string report,
        string combatantScope,
        string displayMode,
        string groupMode,
        string? searchText,
        bool excludeCommonDrops,
        CancellationToken cancellationToken)
    {
        var request = JsonSerializer.Serialize(
            new BridgeRequest(
                1,
                "snapshot",
                null,
                null,
                scope,
                battleId,
                mobName,
                report,
                combatantScope,
                displayMode,
                groupMode,
                searchText,
                excludeCommonDrops),
            JsonOptions);
        return await SendRequestAsync<BridgeSnapshot>(
            request,
            TimeSpan.FromSeconds(3),
            cancellationToken);
    }

    public async Task<BridgeCommandResult> SendCommandAsync(
        string command,
        CancellationToken cancellationToken)
    {
        return await SendCommandAsync(command, null, cancellationToken);
    }

    public async Task<BridgeCommandResult> SendCommandAsync(
        string command,
        string? targetPlayer,
        CancellationToken cancellationToken)
    {
        var request = JsonSerializer.Serialize(
            new BridgeRequest(
                1,
                "command",
                command,
                targetPlayer,
                null,
                0,
                null,
                null,
                null,
                null,
                null,
                null,
                false),
            JsonOptions);
        var timeout = string.Equals(command, "detect", StringComparison.OrdinalIgnoreCase)
            ? TimeSpan.FromSeconds(45)
            : string.Equals(command, "capturestats", StringComparison.OrdinalIgnoreCase)
                ? TimeSpan.FromSeconds(20)
                : TimeSpan.FromSeconds(8);

        return await SendRequestAsync<BridgeCommandResult>(request, timeout, cancellationToken);
    }

    private static async Task<TResponse> SendRequestAsync<TResponse>(
        string request,
        TimeSpan responseTimeoutValue,
        CancellationToken cancellationToken)
        where TResponse : class
    {
        await using var pipe = new NamedPipeClientStream(
            ".",
            PipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous,
            TokenImpersonationLevel.Identification);

        using (var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            connectTimeout.CancelAfter(TimeSpan.FromMilliseconds(350));
            try
            {
                await pipe.ConnectAsync(connectTimeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("The KParser bridge is not available.");
            }
        }

        using var responseTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        responseTimeout.CancelAfter(responseTimeoutValue);

        var requestBytes = Encoding.UTF8.GetBytes(request + "\n");
        await pipe.WriteAsync(requestBytes, responseTimeout.Token);
        await pipe.FlushAsync(responseTimeout.Token);

        using var reader = new StreamReader(
            pipe,
            new UTF8Encoding(false),
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 4096,
            leaveOpen: true);

        var response = await reader.ReadLineAsync(responseTimeout.Token);
        if (string.IsNullOrWhiteSpace(response))
            throw new InvalidDataException("KParser returned an empty bridge response.");

        if (response.Length > MaxResponseCharacters)
            throw new InvalidDataException("KParser returned an oversized bridge response.");

        var result = JsonSerializer.Deserialize<TResponse>(response, JsonOptions)
            ?? throw new InvalidDataException("KParser returned an invalid bridge response.");

        var protocol = result switch
        {
            BridgeSnapshot snapshot => snapshot.Protocol,
            BridgeCommandResult commandResult => commandResult.Protocol,
            _ => 0
        };

        if (protocol != 1)
            throw new InvalidDataException("The KParser bridge protocol is not supported.");

        return result;
    }

    private sealed record BridgeRequest(
        int Protocol,
        string Type,
        string? Command,
        string? TargetPlayer,
        string? Scope,
        int BattleId,
        string? MobName,
        string? Report,
        string? CombatantScope,
        string? DisplayMode,
        string? GroupMode,
        string? SearchText,
        bool ExcludeCommonDrops);
}
