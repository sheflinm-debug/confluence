using System.Collections.Generic;

/// Per-community display names, shown in the HUD (Mine tab, Ranks tab) instead of the bare
/// "Comm N" id. Names are randomly generated on first lookup (reusing KingdomNameGenerator for
/// aesthetic consistency with kingdom names elsewhere) and cached — every community gets a stable
/// name for the run. The player can rename their own community via the Ranks tab (see GameHUD).
public static class CommunityNameRegistry
{
    private static readonly Dictionary<int, string> _names = new Dictionary<int, string>();

    /// Returns this community's display name, generating and caching one on first call.
    public static string GetName(int communityId)
    {
        if (!_names.TryGetValue(communityId, out string name))
        {
            name = KingdomNameGenerator.Generate();
            _names[communityId] = name;
        }
        return name;
    }

    /// Player-driven rename (Ranks tab). Trims and length-caps; ignores empty input rather than
    /// letting a community go nameless.
    public static void SetName(int communityId, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName)) return;
        newName = newName.Trim();
        if (newName.Length > 24) newName = newName.Substring(0, 24);
        _names[communityId] = newName;
    }

    /// Called at the start of a new run (paired with GeneEvolutionManager.ResetCatalog) so a fresh
    /// game doesn't inherit names generated during a previous session.
    public static void Reset() => _names.Clear();
}
