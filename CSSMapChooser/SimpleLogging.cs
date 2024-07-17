using CounterStrikeSharp.API;

namespace CSSMapChooser;

public static class SimpleLogging {
    public static void LogDebug(string information) {
        if(0 >= PluginSettings.GetInstance().cssmcDebugLevel.Value)
            return;

        Server.PrintToConsole("[CCSMC DEBUG] " + information);

        if(!PluginSettings.GetInstance().cssmcDebugShowClientConsole.Value)
            return;

        try {
            foreach(var client in Utilities.GetPlayers()) {
                if(!client.IsValid || client.IsBot || client.IsHLTV)
                    continue;
                
                client.PrintToConsole("[CCSMC DEBUG] " + information);
            }
        } catch(Exception) {}
    }

    public static void LogTrace(string information) {
        if(1 >= PluginSettings.GetInstance().cssmcDebugLevel.Value)
            return;

        Server.PrintToConsole("[CCSMC TRACE] " + information);

        if(!PluginSettings.GetInstance().cssmcDebugShowClientConsole.Value)
            return;

        try {
            foreach(var client in Utilities.GetPlayers()) {
                if(!client.IsValid || client.IsBot || client.IsHLTV)
                    continue;
                
                client.PrintToConsole("[CCSMC TRACE] " + information);
            }
        } catch(Exception) {}
    }
    }