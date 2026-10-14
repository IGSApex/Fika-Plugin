using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Fika.Core.Networking.Http;
using System.Threading.Tasks;

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

    internal static async Task<bool> Invite(string recipientId)
    {
        var response = await FikaRequestHandler.InviteToSquad(recipientId);
        return response != null && response.Success;
    }

    internal static async Task<bool> Respond(string inviteId, bool accept)
    {
        var response = await FikaRequestHandler.RespondToSquadInvite(inviteId, accept);
        if (response == null || !response.Success)
            return false;
        PendingInvites.Remove(inviteId);
        return true;
    }

    internal static async Task Refresh()
    {
        var response = await FikaRequestHandler.GetSquadStatus();
        if (response == null)
            return;
        SquadId = response.SquadId ?? string.Empty;
        Members = response.Members ?? Array.Empty<string>();
        PendingInvites.Clear();
        if (response.PendingInvites != null)
            foreach (var invite in response.PendingInvites)
                PendingInvites[invite.Id] = invite.Sender;
        StateChanged?.Invoke();
    }

    internal static async Task<bool> Leave()
    {
        var response = await FikaRequestHandler.LeaveSquad();
        if (response == null || !response.Success)
            return false;
        await Refresh();
        return true;
    }

    internal static void Receive(JObject payload)
    {
        var rawType = payload.Value<string>("type");
        if (!Enum.TryParse(rawType, true, out EFikaNotification type))
            return;
        if (type == EFikaNotification.SquadInvite)
        {
            var inviteId = payload.Value<string>("inviteId");
            var sender = payload.Value<string>("senderId");
            if (!string.IsNullOrEmpty(inviteId) && !string.IsNullOrEmpty(sender))
                PendingInvites[inviteId] = sender;
        }
        else if (type == EFikaNotification.SquadUpdated)
        {
            SquadId = payload.Value<string>("squadId") ?? string.Empty;
            Members = payload["members"]?.ToObject<string[]>() ?? Array.Empty<string>();
            // Raid grouping is resolved separately from the matchmaking lobby.
        }
        StateChanged?.Invoke();
    }
}
