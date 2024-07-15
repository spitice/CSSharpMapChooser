using System.Text.RegularExpressions;
using CounterStrikeSharp.API.Core;
using Microsoft.Extensions.Logging;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API;

namespace CSSMapChooser;

public class RockTheVote {
    
    private readonly CSSMapChooser plugin;
    private MapConfig mapConfig;

    public RockTheVote(CSSMapChooser plugin, MapConfig mapConfig) {
        this.plugin = plugin;
        this.mapConfig = mapConfig;
        plugin.AddCommand("css_forcertv", "Initiate the force rtv", CommandForceRTV);
    }

    [RequiresPermissions(@"css/map")]
    private void CommandForceRTV(CCSPlayerController? client, CommandInfo info) {
        if(client == null)
            return;

        VoteManager? voteManager = plugin.GetVoteManager();

        if(voteManager != null && voteManager.GetVoteProgress() == VoteManager.VoteProgress.VOTE_STARTING ||
            voteManager != null && voteManager.GetVoteProgress() == VoteManager.VoteProgress.VOTE_IN_PROGRESS
        ) {
            client.PrintToChat($"{plugin.CHAT_PREFIX} Vote is in progress!");
            return;
        }

        if(voteManager != null && voteManager.GetNextMap() != null) {
            ForceChangeMapWithRTV();
            return;
        }

        VoteManager newVoteManager = new VoteManager(plugin.GetNominationModule(), mapConfig.GetMapDataList(), plugin, true);

        plugin.SetVoteManager(newVoteManager);

        newVoteManager.StartVoteProcess();
    }

    private bool ForceChangeMapWithRTV() {
        VoteManager? voteManager = plugin.GetVoteManager();
        MapData? mapData = voteManager?.GetNextMap();

        if(mapData == null)
            return false;

        Server.PrintToChatAll($"{plugin.CHAT_PREFIX} Changing map to {mapData.MapName}! Rock The Vote has spoken!");

        plugin.AddTimer(PluginSettings.GetInstance().cssmcRTVMapChangingDelay.Value, () => {
            plugin.ChangeToNextMap(mapData);
        }, CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);
        return true;
    }
}