using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Fika.Core.Main.Utils;

namespace Fika.Core.Networking.Websocket;

/// <summary>
/// Incoming squad state is separate from raid peer membership. The game UI must
/// explicitly accept invitations before a player is assigned to a squad.
/// </summary>
internal static class FikaSquadNotifications
{
    internal static readonly Dictionary<string, string> PendingInvites = new();
    internal static string SquadId { get; private set; } = string.Empty;
    internal static string[] Members { get; private set; } = Array.Empty<string>();

    internal static event Action StateChanged;

    internal static void Receive(JObject payload)
    {
        var type = payload.Value<int>("type");
        if (type == (int)EFikaNotification.SquadInvite)
        {
            var inviteId = payload.Value<string>("inviteId");
            var sender = payload.Value<string>("senderId");
            if (!string.IsNullOrEmpty(inviteId) && !string.IsNullOrEmpty(sender))
                PendingInvites[inviteId] = sender;
        }
        else if (type == (int)EFikaNotification.SquadUpdated)
        {
            SquadId = payload.Value<string>("squadId") ?? string.Empty;
            Members = payload["members"]?.ToObject<string[]>() ?? Array.Empty<string>();
            // Raid grouping is resolved separately from the matchmaking lobby.
            FikaBackendUtils.GroupId = SquadId;
        }
        StateChanged?.Invoke();
    }
}
