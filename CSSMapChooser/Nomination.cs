using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;

namespace CSSMapChooser;

public class Nomination {
    private List<NominationData> nominatedMaps = new();

    public List<NominationData> GetNominatedMaps() {
        return nominatedMaps;
    }

    private CSSMapChooser plugin;
    private MapConfig mapConfig;

    enum NominationStatus {
        NOMINATION_SUCCESS,
        NOMINATION_DUPLICATE,
        NOMINATION_CHANGED,
        NOMINATION_FORCED,
    }

    public Nomination(CSSMapChooser plugin, MapConfig mapConfig) {
        this.plugin = plugin;
        this.mapConfig = mapConfig;
        plugin.AddCommand("css_nominate", "nominate the specified map", CommandNominate);
        plugin.AddCommand("css_nominate_addmap", "Insert map to nomination", CommandNominateAddMap);
        plugin.AddCommand("css_nominate_removemap", "Remove map from nomination", CommandNominateRemoveMap);
        plugin.AddCommand("css_nomlist", "Show nomination list", CommandNomList);
    }

    public void initializeNominations() {
        nominatedMaps.Clear();
    }

    private void CommandNominate(CCSPlayerController? client, CommandInfo info) {
        if(client == null)
            return;
        
        MapData? newNominationMap = null;

        string mapName = info.GetArg(1);

        if(mapName.Equals("")) {
            client.PrintToChat($"{plugin.CHAT_PREFIX} css_nominate <map name>");
            return;
        }

        foreach(MapData mapData in mapConfig.GetMapDataList()) {
            if(mapData.MapName.Equals(mapName, StringComparison.OrdinalIgnoreCase)) {
                newNominationMap = mapData;
                break;
            }
        }

        if(newNominationMap == null) {
            client.PrintToChat($"{plugin.CHAT_PREFIX} No maps found with {mapName}");
            return;
        }


        NominationData? existingMapNomination = null;
        
        foreach(NominationData nominatedMap in nominatedMaps) {
            if(nominatedMap.mapData.MapName.Equals(newNominationMap.MapName, StringComparison.OrdinalIgnoreCase)) {
                existingMapNomination = nominatedMap;
            }
        }

        if(existingMapNomination != null) {
            NominationStatus nominationStatus = NominateMap(existingMapNomination, client);

            switch(nominationStatus) {
                case NominationStatus.NOMINATION_SUCCESS: {
                    Server.PrintToChatAll($"{plugin.CHAT_PREFIX} {client.PlayerName} nominated {existingMapNomination.mapData.MapName}. Nomination counts: {existingMapNomination.GetNominators().Count()}");
                    break;
                }
                case NominationStatus.NOMINATION_DUPLICATE: {
                    client.PrintToChat($"{plugin.CHAT_PREFIX} You already nominated the {existingMapNomination.mapData.MapName}!");
                    break;
                }
                case NominationStatus.NOMINATION_CHANGED: {
                    Server.PrintToChatAll($"{plugin.CHAT_PREFIX} {client.PlayerName} changed their nomination to {existingMapNomination.mapData.MapName}. Nomination counts: {existingMapNomination.GetNominators().Count()}");
                    break;
                }
                case NominationStatus.NOMINATION_FORCED: {
                    client.PrintToChat($"{plugin.CHAT_PREFIX} {existingMapNomination.mapData.MapName} is already nominated by admin!");
                    break;
                }
            }

        }
        else {
            NominationData newNomination = new NominationData(newNominationMap);
            nominatedMaps.Add(newNomination);

            NominationStatus nominationStatus = NominateMap(newNomination, client);

            switch(nominationStatus) {
                case NominationStatus.NOMINATION_SUCCESS: {
                    Server.PrintToChatAll($"{plugin.CHAT_PREFIX} {client.PlayerName} nominated {newNomination.mapData.MapName}");
                    break;
                }
                case NominationStatus.NOMINATION_CHANGED: {
                    Server.PrintToChatAll($"{plugin.CHAT_PREFIX} {client.PlayerName} changed their nomination to {newNomination.mapData.MapName}");
                    break;
                }
            }
        }
    }


    private void CommandNominateAddMap(CCSPlayerController? client, CommandInfo info) {
        if(client == null)
            return;
        
        MapData? newNominationMap = null;

        string mapName = info.GetArg(1);

        if(mapName.Equals("")) {
            client.PrintToChat($"{plugin.CHAT_PREFIX} css_nominate_addmap <map name>");
            return;
        }

        foreach(MapData mapData in mapConfig.GetMapDataList()) {
            if(mapData.MapName.Equals(mapName, StringComparison.OrdinalIgnoreCase)) {
                newNominationMap = mapData;
                break;
            }
        }

        if(newNominationMap == null) {
            client.PrintToChat($"{plugin.CHAT_PREFIX} No maps found with {mapName}");
            return;
        }


        NominationData? existingMapNomination = null;
        
        foreach(NominationData nominatedMap in nominatedMaps) {
            if(nominatedMap.mapData.MapName.Equals(newNominationMap.MapName, StringComparison.OrdinalIgnoreCase)) {
                existingMapNomination = nominatedMap;
            }
        }

        if(existingMapNomination != null) {
            client.PrintToChat($"{plugin.CHAT_PREFIX} Map {existingMapNomination.mapData.MapName} is already nominated!");
        }
        else {
            NominationData newNomination = new NominationData(newNominationMap, true);
            nominatedMaps.Add(newNomination);

            Server.PrintToChatAll($"{plugin.CHAT_PREFIX} Admin {client.PlayerName} inserted {newNomination.mapData.MapName} to nomination");
        }
    }

    private void CommandNominateRemoveMap(CCSPlayerController? client, CommandInfo info) {
        if(client == null)
            return;

        string mapName = info.GetArg(1);

        if(mapName.Equals("")) {
            client.PrintToChat($"{plugin.CHAT_PREFIX} css_nominate_removemap <map name>");
            return;
        }

        NominationData? existingMapNomination = null;
        
        foreach(NominationData nominatedMap in nominatedMaps) {
            if(nominatedMap.mapData.MapName.Equals(mapName, StringComparison.OrdinalIgnoreCase)) {
                existingMapNomination = nominatedMap;
            }
        }

        if(existingMapNomination == null) {
            client.PrintToChat($"{plugin.CHAT_PREFIX} Map {mapName} is not nominated!");
        }
        else {
            Server.PrintToChatAll($"{plugin.CHAT_PREFIX} Admin {client.PlayerName} removed {mapName} from nomination");
            nominatedMaps.Remove(existingMapNomination);
        }
    }

    private NominationData? FindExistingClientNomination(CCSPlayerController client) {
        foreach(NominationData nominationData in nominatedMaps) {
            if(nominationData.GetNominators().Contains(client))
                return nominationData;
        }

        return null;
    }

    private void CommandNomList(CCSPlayerController? client, CommandInfo info) {
        if(client == null)
            return;

        if(nominatedMaps.Count() == 0) {
            client.PrintToChat($"{plugin.CHAT_PREFIX} There is no nominated maps!");
            return;
        }

        bool shouldShowFullList = info.GetArg(1).Equals("full", StringComparison.OrdinalIgnoreCase);

        if(shouldShowFullList) {
            client.PrintToChat($"{plugin.CHAT_PREFIX} ===== Nominations (FULL) =====");

            foreach(NominationData nominationData in nominatedMaps) {
                if(nominationData.isForceNominate) {
                    client.PrintToChat($"{plugin.CHAT_PREFIX} {nominationData.mapData.MapName}: {ChatColors.Red}Admin");
                }
                else {
                    client.PrintToChat($"{plugin.CHAT_PREFIX} {nominationData.mapData.MapName}: {ChatColors.Lime}{string.Join($"{ChatColors.Default}, {ChatColors.Lime}" , nominationData.GetNominators().Select(p => p.PlayerName))}");
                }
            }

        }
        else {
            client.PrintToChat($"{plugin.CHAT_PREFIX} ===== Nominations =====");

            foreach(NominationData nominationData in nominatedMaps) {
                if(nominationData.isForceNominate) {
                    client.PrintToChat($"{plugin.CHAT_PREFIX} {nominationData.mapData.MapName}: {ChatColors.Red}Admin");
                }
                else {
                    client.PrintToChat($"{plugin.CHAT_PREFIX} {nominationData.mapData.MapName}: {ChatColors.Lime}{nominationData.GetNominators().Count()} votes");
                }
            }
        }

    }

    private NominationStatus NominateMap(NominationData nomination, CCSPlayerController nominator) {
        if(nomination.isForceNominate)
            return NominationStatus.NOMINATION_FORCED;

        NominationData? existingClientNomination = FindExistingClientNomination(nominator);

        if(existingClientNomination == null) {
            nomination.AddNominator(nominator);
            return NominationStatus.NOMINATION_SUCCESS;
        }
        else {

            if(existingClientNomination.GetNominators().Contains(nominator) && existingClientNomination.mapData.MapName.Equals(nomination.mapData.MapName, StringComparison.OrdinalIgnoreCase)) {
                return NominationStatus.NOMINATION_DUPLICATE;
            }

            existingClientNomination.RemoveNominator(nominator);
            if(existingClientNomination.GetNominators().Count() <= 0)
                nominatedMaps.Remove(existingClientNomination);

            nomination.AddNominator(nominator);
            return NominationStatus.NOMINATION_CHANGED;
        }
    }
}