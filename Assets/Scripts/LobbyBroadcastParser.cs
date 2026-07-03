/// Parses the EVOSIM_LOBBY broadcast string produced by LANDiscovery.BroadcastLobbyState
/// and applies it to the MainMenuManager's slot arrays.
public static class LobbyBroadcastParser
{
    // Format: EVOSIM_LOBBY:seed:type0,type1,...:ready0,ready1,...
    public static void Apply(string raw, MainMenuManager.SlotType[] slots, bool[] ready)
    {
        if (string.IsNullOrEmpty(raw)) return;
        string[] sections = raw.Split(':');
        // sections[0] = "EVOSIM_LOBBY", [1] = seed, [2] = types, [3] = ready flags
        if (sections.Length < 4) return;

        string[] typeTokens  = sections[2].Split(',');
        string[] readyTokens = sections[3].Split(',');

        for (int i = 0; i < slots.Length && i < typeTokens.Length; i++)
        {
            if (int.TryParse(typeTokens[i], out int t))
                slots[i] = (MainMenuManager.SlotType)t;
        }
        for (int i = 0; i < ready.Length && i < readyTokens.Length; i++)
        {
            ready[i] = readyTokens[i] == "1";
        }
    }
}
