using System;
using System.Collections.Generic;
using System.Linq;
using Fika.Core.Main.Utils;
using Fika.Core.Networking.Websocket;

namespace Fika.Core.API;

/// <summary>
/// Public, read-only squad API for third-party BepInEx mods.
/// This client-side view only knows the authenticated local player's squad.
/// </summary>
public static class FikaSquadAPI
{
    /// <summary>Raised when the local squad snapshot changes.</summary>
    public static event Action StateChanged
    {
        add => FikaSquadNotifications.StateChanged += value;
        remove => FikaSquadNotifications.StateChanged -= value;
    }

    /// <summary>Returns the local player's squad ID, or null when not in a squad.</summary>
    public static string GetLocalSquadId() =>
        string.IsNullOrEmpty(FikaSquadNotifications.SquadId) ? null : FikaSquadNotifications.SquadId;

    /// <summary>Returns a snapshot of the local player's squad profile IDs.</summary>
    public static string[] GetLocalSquadMembers() =>
        (string[])FikaSquadNotifications.Members.Clone();

    /// <summary>Whether the given profile ID belongs to the local player's squad.</summary>
    public static bool IsInLocalSquad(string profileId) =>
        !string.IsNullOrWhiteSpace(GetLocalSquadId()) &&
        !string.IsNullOrWhiteSpace(profileId) &&
        FikaSquadNotifications.Members.Contains(profileId, StringComparer.Ordinal);

    /// <summary>
    /// Returns true if all 2-5 distinct profile IDs belong to the local player's squad.
    /// This cannot determine membership of a remote squad that the local player is not in.
    /// </summary>
    public static bool AreSquadmates(params string[] profileIds)
    {
        if (profileIds == null || profileIds.Length < 2 || profileIds.Length > 5 ||
            string.IsNullOrWhiteSpace(GetLocalSquadId()))
            return false;

        var members = new HashSet<string>(FikaSquadNotifications.Members, StringComparer.Ordinal);
        var requested = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in profileIds)
        {
            if (string.IsNullOrWhiteSpace(id) || !requested.Add(id) || !members.Contains(id))
                return false;
        }

        return true;
    }
}
