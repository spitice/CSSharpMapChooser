using CounterStrikeSharp.API;

namespace CSSMapChooser;

public static class SimpleLogging {
        public static void LogDebug(string information) {
            foreach(var client in Utilities.GetPlayers()) {
                if(!client.IsValid || client.IsBot || client.IsHLTV)
                    continue;
                
                client.PrintToConsole("[CCSMC DEBUG] " + information);
            }
            Server.PrintToConsole("[CCSMC DEBUG] " + information);
        }

        public static void LogTrace(string information) {
        foreach(var client in Utilities.GetPlayers()) {
            if(!client.IsValid || client.IsBot || client.IsHLTV)
                continue;
            
            client.PrintToConsole("[CCSMC TRACE] " + information);
        }
        Server.PrintToConsole("[CCSMC TRACE] " + information);
        }
    }