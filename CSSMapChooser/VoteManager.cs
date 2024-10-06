using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Menu;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace CSSMapChooser;

public class VoteManager {

    private CSSMapChooser plugin;

    public enum VoteProgress {
        VOTE_PENDING,
        VOTE_INITIATING,
        VOTE_IN_PROGRESS,
        VOTE_FINISHED,
        VOTE_NEXTMAP_CONFIRMED,
    }

    private bool isActivatedByRTV = false;

    private List<MapData> mapList = new ();


    public MapData? nextMap {get; private set;} = null;

    public VoteProgress voteProgress {get; private set;} = VoteProgress.VOTE_PENDING;
    
    public bool shouldRestartAfterRoundEnd {get; private set;} = false;

    private List<MapVoteData> votingMaps = new ();
    private List<MapVoteData>? runoffVoteMaps = null;

    private Timer? countdownTimer = null;
    private Timer? voteTimer = null;
    private Random random = new ();

    private string TEMP_VOTE_MAP_DONT_CHANGE = "Don't Change";
    private string TEMP_VOTE_MAP_EXTEND_MAP = "Extend Current Map";

    private int _votingMapSlotsRemaining {
        get {
            return PluginSettings.GetInstance().cssmcMapVoteMapCount.Value - votingMaps.Count();
        }
    }


    public VoteManager(List<MapData> mapList, CSSMapChooser plugin, bool isActivatedByRTV = false) {
        this.mapList = mapList;
        this.plugin = plugin;
        this.isActivatedByRTV = isActivatedByRTV;
    }

    public void StartVoteProcess() {
        if(voteProgress != VoteProgress.VOTE_PENDING)
            throw new InvalidOperationException("Vote is already in progress and cannot be start twice!");
        
        voteProgress = VoteProgress.VOTE_INITIATING;
        
        double countdownStartTime = Server.EngineTime;

        SimpleLogging.LogDebug($"Initiating vote countdown. seconds: {PluginSettings.GetInstance().cssmcMapVoteCountdownTime.Value}");
        Server.PrintToChatAll($"{plugin.CHAT_PREFIX} Vote starting in {PluginSettings.GetInstance().cssmcMapVoteCountdownTime.Value} seconds");

        countdownTimer = plugin.AddTimer(1.0F, () => {
            var elapsedTime = Server.EngineTime - countdownStartTime;
            var remainingTime = PluginSettings.GetInstance().cssmcMapVoteCountdownTime.Value - elapsedTime;

            if(remainingTime <= 0.9) {
                InitiateVote();
                countdownTimer?.Kill();
            }
            else if(remainingTime <= 10.0) {
                Server.PrintToChatAll($"{plugin.CHAT_PREFIX} Vote starting in {remainingTime:#} seconds");
            }
            
        }, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
    }

    private void InitiateVote() {
        voteProgress = VoteProgress.VOTE_IN_PROGRESS;
        double voteStartTime = Server.EngineTime;
        double voteTime = PluginSettings.GetInstance().cssmcMapVoteTime.Value;

        SimpleLogging.LogDebug("Start voting.");

        
        SimpleLogging.LogDebug("Initializing voting map list");
        votingMaps.Clear();

        if(isActivatedByRTV) {
            votingMaps.Add(new MapVoteData(new MapData(TEMP_VOTE_MAP_DONT_CHANGE, false)));
        }
        else {
            if(plugin.extendsCount < PluginSettings.GetInstance().cssmcMapVoteAvailableExtends.Value) {
                votingMaps.Add(new MapVoteData(new MapData(TEMP_VOTE_MAP_EXTEND_MAP, false)));
            }
        }

        if(runoffVoteMaps == null) {
            SimpleLogging.LogDebug("This is a initial vote");

            HashSet<MapData> unusedMapPool = new HashSet<MapData>(mapList);

            void addToVotingMaps(MapData mapData) {
                if (!unusedMapPool.Contains(mapData)) {
                    // The map is already added to voting maps
                    return;
                }

                votingMaps.Add(new MapVoteData(mapData));
                unusedMapPool.Remove(mapData);
            }
            
            List<NominationData> sortedNominatedMaps = plugin.nominationModule.GetNominatedMaps()
                .OrderByDescending(v => v.GetNominators().Count())
                .ToList();

            List<NominationData> adminNominations = plugin.nominationModule.GetNominatedMaps()
                .Where(v => v.isForceNominate)
                .ToList();

            foreach(NominationData nominated in adminNominations) {
                if(_votingMapSlotsRemaining <= 0) {
                    break;
                }

                addToVotingMaps(nominated.mapData);
            }

            foreach(NominationData nominated in sortedNominatedMaps) {
                if (_votingMapSlotsRemaining <= 0) {
                    break;
                }

                addToVotingMaps(nominated.mapData);
            }

            // After processing the nominated maps, remove the current map from the unused map pool
            // so it won't be shown in randomly picked maps
            unusedMapPool.RemoveWhere(map => map.MapName.Equals(Server.MapName, StringComparison.OrdinalIgnoreCase));

            if (_votingMapSlotsRemaining > 0) {
                // Fill the remaining slots with randomly picked maps
                var numToPick = Math.Min(_votingMapSlotsRemaining, unusedMapPool.Count);
                SimpleLogging.LogDebug($"{numToPick} maps will be chosen randomely");
                var unusedMapList = unusedMapPool.ToList();
                var pickedMaps = unusedMapList.OrderBy(_ => random.Next()).Take(numToPick);
                foreach (var map in pickedMaps) {
                    addToVotingMaps(map);
                }
            }
        }
        else {
            SimpleLogging.LogDebug("This is a runoff vote");
            votingMaps = new List<MapVoteData>(runoffVoteMaps);
            foreach(MapVoteData vote in votingMaps) {
                vote.ResetVotes();
            }
        }
        SimpleLogging.LogDebug("Voting map list initialized");

        SimpleLogging.LogTrace("Vote targets:");
        foreach(MapVoteData maps in votingMaps) {
            SimpleLogging.LogTrace($"Name: {maps.mapData.MapName}, workshop: {maps.mapData.isWorkshopMap}");
        }

        foreach(CCSPlayerController client in Utilities.GetPlayers()) {
            if(!client.IsValid || client.IsBot || client.IsHLTV)
                continue;
            
            ShowVotingMenu(client);
        }

        voteTimer = plugin.AddTimer(1.0F, () => {
            if(Server.EngineTime - voteStartTime < voteTime) {
                return;
            }

            EndVote();
        }, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
    }

    public void EndVote() {
        if(voteTimer == null)
            return;

        if(voteProgress != VoteProgress.VOTE_IN_PROGRESS)
            return;
        
        foreach(CCSPlayerController cl in Utilities.GetPlayers()) {
            if(!cl.IsValid || cl.IsBot || cl.IsHLTV)
                continue;

            MenuManager.CloseActiveMenu(cl);
        }

        plugin.nominationModule.initializeNominations();
        plugin.rockTheVoteModule.ResetRTVStatus();
        voteTimer.Kill();

        SimpleLogging.LogDebug("Vote ended");
        SimpleLogging.LogTrace("Vote results:");
        foreach(MapVoteData maps in votingMaps) {
            SimpleLogging.LogTrace($"Votes: {maps.GetVoteCounts()}, Name: {maps.mapData.MapName}, workshop: {maps.mapData.isWorkshopMap}");
        }

        List<MapVoteData> winners = PickVoteWinningMaps();

        SimpleLogging.LogTrace($"Winner count: {winners.Count()}");

        foreach(MapVoteData winMap in winners) {
            SimpleLogging.LogTrace($"Votes: {winMap.GetVoteCounts()}, Name: {winMap.mapData.MapName}, workshop: {winMap.mapData.isWorkshopMap}");
        }

        int totalVotes = getTotalVotes();

        if(isActivatedByRTV && totalVotes == 0) {
            SimpleLogging.LogDebug("There is no votes. Extending timelimit...");
            Server.PrintToChatAll($"{plugin.CHAT_PREFIX} There is no votes. Extending timelimit...");
            voteProgress = VoteProgress.VOTE_FINISHED;
            plugin.ExtendCurrentMap(15);
            return;
        }

        MapVoteData topVoteMap = votingMaps.OrderByDescending(v => v.GetVoteCounts()).First();
        int topVotes = topVoteMap.GetVoteCounts();

        float percentageOfTopVotes;

        if(topVotes == 0) {
            percentageOfTopVotes = 0.0F;
        } else {
            percentageOfTopVotes = topVotes / totalVotes;
        }


        if(runoffVoteMaps == null && winners.Count > 1 && percentageOfTopVotes < PluginSettings.GetInstance().cssmcMapVoteRunoffThreshold.Value) {
            SimpleLogging.LogDebug($"No map got over {PluginSettings.GetInstance().cssmcMapVoteRunoffThreshold.Value * 100:F0}% of votes, starting runoff vote");
            Server.PrintToChatAll($"{plugin.CHAT_PREFIX} No map got over {PluginSettings.GetInstance().cssmcMapVoteRunoffThreshold.Value * 100:F0}% of votes, starting runoff vote");
            runoffVoteMaps = winners;
            StartVoteProcess();
            return;
        }

        if(topVoteMap.mapData.MapName.Equals(TEMP_VOTE_MAP_DONT_CHANGE, StringComparison.OrdinalIgnoreCase) && totalVotes != 0) {
            SimpleLogging.LogDebug("Players chose don't change. Waiting for next map vote");
            Server.PrintToChatAll($"{plugin.CHAT_PREFIX} Voting finished.");
            Server.PrintToChatAll($"{plugin.CHAT_PREFIX} Map will not change ({topVoteMap.GetVoteCounts()} votes of {totalVotes} total votes)");
            voteProgress = VoteProgress.VOTE_PENDING;
            return;
        }
        else if(topVoteMap.mapData.MapName.Equals(TEMP_VOTE_MAP_EXTEND_MAP, StringComparison.OrdinalIgnoreCase) && totalVotes != 0) {
            SimpleLogging.LogDebug("Players chose extend map");
            Server.PrintToChatAll($"{plugin.CHAT_PREFIX} Voting finished.");
            Server.PrintToChatAll($"{plugin.CHAT_PREFIX} Extending Current Map ({topVoteMap.GetVoteCounts()} votes of {totalVotes} total votes)");
            plugin.ExtendCurrentMap(15);
            plugin.incrementExtendsCount();
            voteProgress = VoteProgress.VOTE_PENDING;
            return;
        }

        nextMap = topVoteMap.mapData;
        SimpleLogging.LogDebug($"Winner: {nextMap.MapName}");

        Server.PrintToChatAll($"{plugin.CHAT_PREFIX} Voting finished.");
        Server.PrintToChatAll($"{plugin.CHAT_PREFIX} Next map: {nextMap.MapName} ({topVoteMap.GetVoteCounts()} votes of {totalVotes} total votes)");

        voteProgress = VoteProgress.VOTE_NEXTMAP_CONFIRMED;
        if(!isActivatedByRTV)
            return;

        if(PluginSettings.GetInstance().cssmcRTVMapChangingAfterRoundEnd.Value) {
            shouldRestartAfterRoundEnd = true;
        }
        else {
            plugin.AddTimer(PluginSettings.GetInstance().cssmcRTVMapChangingDelay.Value, () => {
                plugin.ChangeToNextMap(nextMap);
            }, TimerFlags.STOP_ON_MAPCHANGE);
        }
    }

    public void CancelVote(CCSPlayerController? client) {
        if(voteProgress != VoteProgress.VOTE_IN_PROGRESS) 
            return;

        SimpleLogging.LogDebug("Cancelling the vote");

        if(client != null) {
            plugin.Logger.LogInformation($"Admin {client.PlayerName} cancelled the current vote");
            Server.PrintToChatAll($"{plugin.CHAT_PREFIX} Admin {ChatColors.Lime}{client.PlayerName}{ChatColors.Default} cancelled the current vote");
        }
        else {
            plugin.Logger.LogInformation($"Cancelled the current vote");
            Server.PrintToChatAll($"{plugin.CHAT_PREFIX} Cancelled the current vote");
        }

        foreach(CCSPlayerController cl in Utilities.GetPlayers()) {
            if(!cl.IsValid || cl.IsBot || cl.IsHLTV)
                continue;

            MenuManager.CloseActiveMenu(cl);
        }

        if((int)PluginSettings.GetInstance().cssmcMapVoteStartTime.Value < plugin.timeleft) {
            voteProgress = VoteProgress.VOTE_PENDING;
        } else {
            voteProgress = VoteProgress.VOTE_FINISHED;
        }


        voteTimer?.Kill();
        countdownTimer?.Kill();
        plugin.nominationModule.initializeNominations();
        plugin.rockTheVoteModule.ResetRTVStatus();
        SimpleLogging.LogDebug("Vote cancelled");
    }

    private List<MapVoteData> PickVoteWinningMaps() {
        SimpleLogging.LogDebug("Picking winners");
        List<MapVoteData> winners = new ();

        List<MapVoteData> sortedVotingMaps = votingMaps
            .OrderByDescending(v => v.GetVoteCounts())
            .ToList();

        int topVotes = sortedVotingMaps.First().GetVoteCounts();
        SimpleLogging.LogTrace($"Top vote: {sortedVotingMaps.First().mapData.MapName}, {topVotes} votes");

        float winnerPickupThreshold = PluginSettings.GetInstance().cssmcMapVoteWinnerPickupThreshold.Value;

        int totalVotes = 0;

        foreach(MapVoteData map in votingMaps) {
            totalVotes += map.GetVoteCounts();
        }

        SimpleLogging.LogDebug("Total vote is 0! Returning the first element of list because we cannot divide with 0.");
        if(totalVotes == 0) {
            List<MapVoteData> top = [sortedVotingMaps.First()];
            return top;
        }

        foreach(MapVoteData map in sortedVotingMaps) {
                                    // Those float cast is required to calculation
            float votePercentage = (float)map.GetVoteCounts() / (float)totalVotes;


            SimpleLogging.LogTrace($"Vote {votePercentage*100:F0}% > Threshold {winnerPickupThreshold*100:F0}%");
            if(votePercentage > winnerPickupThreshold) {
                winners.Add(map);
            }
        }

        return winners;
    }

    public void ShowVotingMenu(CCSPlayerController client) {
        CenterHtmlMenu menu = new CenterHtmlMenu("MapVote", plugin);

        if(nextMap != null || voteProgress != VoteProgress.VOTE_IN_PROGRESS)
            return;

        foreach(MapVoteData maps in votingMaps) {
            menu.AddMenuOption(maps.mapData.MapName, (controller, option) => {
                ProcessPlayerVote(controller, option.Text);
            });
        }

        MenuManager.OpenCenterHtmlMenu(plugin, client, menu);
    }

    private void ProcessPlayerVote(CCSPlayerController client, string mapName) {
        MenuManager.CloseActiveMenu(client);
        if(voteProgress != VoteProgress.VOTE_IN_PROGRESS)
            return;

        SimpleLogging.LogDebug("Start processing the player vote");

        MapVoteData? existingPlayerVote = null;
        MapVoteData? votingTarget = null;

        SimpleLogging.LogDebug("Iterating voting maps");
        foreach(MapVoteData map in votingMaps) {
            if(map.GetVotedPlayers().Contains(client))
                existingPlayerVote = map;
            
            if(map.mapData.MapName.Equals(mapName, StringComparison.OrdinalIgnoreCase))
                votingTarget = map;
        }

        if(votingTarget == null) {
            SimpleLogging.LogDebug($"{client.PlayerName} specified map {mapName} is not exists in current vote");
            client.PrintToChat($"Map {mapName} is not exists in current vote!");
            return;
        }

        if(existingPlayerVote == null) {
            votingTarget.AddVotedPlayer(client);
            SimpleLogging.LogDebug($"{client.PlayerName} voted to {mapName}");
            Server.PrintToChatAll($"{plugin.CHAT_PREFIX} {client.PlayerName} voted to {mapName}");
        }
        else {
            if(existingPlayerVote.mapData.MapName.Equals(mapName, StringComparison.OrdinalIgnoreCase)) {
                client.PrintToChat($"{plugin.CHAT_PREFIX} You already voted to {mapName}!");
                return;
            }
            SimpleLogging.LogDebug($"Player is already voted to {existingPlayerVote.mapData.MapName} removing.");
            existingPlayerVote.RemoveVotedPlayer(client);
            votingTarget.AddVotedPlayer(client);
            SimpleLogging.LogDebug($"{client.PlayerName} voted to {mapName}");
            Server.PrintToChatAll($"{plugin.CHAT_PREFIX} {client.PlayerName} voted to {mapName}");
        }





        if(getTotalVotes() >= plugin.getHumanPlayersCount()) {
            EndVote();
        }
    }

    private int getTotalVotes() {
        int totalVotes = 0;

        foreach(MapVoteData map in votingMaps) {
            totalVotes += map.GetVoteCounts();
        }

        return totalVotes;
    }
}