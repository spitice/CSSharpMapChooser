using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Menu;
using CounterStrikeSharp.API.Modules.Timers;
using Microsoft.Extensions.Logging;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace CSSMapChooser;

public class VoteManager {

    private CSSMapChooser plugin;

    private bool isVoteInProgress = false;
    private bool isRunoffVoteTriggered = false;
    
    private bool shouldRestartAfterRoundEnd = false;
    private bool isActivatedByRTV;


    private Nomination nominationModule;
    private List<MapData> mapList;

    private List<VoteState> votingMaps = new ();
    List<VoteState>? runoffVoteMaps = null;

    private MapData? nextMap = null;

    private Timer? voteTimer = null;
    Timer? countdownTimer = null;
    private Random random = new ();

    private string TEMP_VOTE_MAP_DONT_CHANGE = "Don't Change";
    private string TEMP_VOTE_MAP_EXTEND_MAP = "Extend Current Map";

    public VoteManager(Nomination nominationModule, List<MapData> mapList, CSSMapChooser plugin, bool isActivatedByRTV = false) {
        this.nominationModule = nominationModule;
        this.mapList = mapList;
        this.plugin = plugin;
        this.isActivatedByRTV = isActivatedByRTV;
    }

    public void StartVoteProcess() {
        isVoteInProgress = true;
        
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
        double voteStartTime = Server.EngineTime;
        double TEMP_CVAR_VALUE_VOTE_TIME = 30.0D;
        SimpleLogging.LogDebug("Start voting.");

        
        SimpleLogging.LogDebug("Initializing voting map list");
        votingMaps.Clear();

        if(isActivatedByRTV) {
            votingMaps.Add(new VoteState(new MapData(TEMP_VOTE_MAP_DONT_CHANGE, false)));
        }
        else {
            votingMaps.Add(new VoteState(new MapData(TEMP_VOTE_MAP_EXTEND_MAP, false)));
        }

        if(runoffVoteMaps == null) {
            SimpleLogging.LogDebug("This is a initial vote");

            List<NominationData> sortedNominatedMaps = nominationModule.GetNominatedMaps()
                .OrderByDescending(v => v.GetNominators().Count())
                .ToList();

            List<NominationData> adminNominations = nominationModule.GetNominatedMaps()
                .Where(v => v.isForceNominate)
                .ToList();

            foreach(NominationData nominated in adminNominations) {
                if(votingMaps.Count() > 8)
                    break;

                votingMaps.Add(new VoteState(nominated.mapData));
            }

            foreach(NominationData nominated in sortedNominatedMaps) {
                if(votingMaps.Count() > 8)
                    break;

                bool isAlreadyNominated = false;
                foreach(VoteState map in votingMaps) {
                    if(map.mapData.MapName.Equals(nominated.mapData.MapName, StringComparison.OrdinalIgnoreCase)) {
                        isAlreadyNominated = true;
                        break;
                    }
                }

                if(isAlreadyNominated)
                    continue;

                votingMaps.Add(new VoteState(nominated.mapData));
            }

            while(votingMaps.Count() < 8) {
                int index = random.Next(mapList.Count());
                MapData pickedMap = mapList[index];

                bool isAlreadyNominated = false;
                foreach(VoteState map in votingMaps) {
                    if(map.mapData.MapName.Equals(pickedMap.MapName, StringComparison.OrdinalIgnoreCase)) {
                        isAlreadyNominated = true;
                        break;
                    }
                }

                if(isAlreadyNominated)
                    continue;

                votingMaps.Add(new VoteState(pickedMap));
            }
        }
        else {
            SimpleLogging.LogDebug("This is a runoff vote");
            votingMaps = new List<VoteState>(runoffVoteMaps);
            foreach(VoteState vote in votingMaps) {
                vote.ResetVotes();
            }
        }
        SimpleLogging.LogDebug("Voting map list initialized");

        SimpleLogging.LogTrace("Vote targets:");
        foreach(VoteState maps in votingMaps) {
            SimpleLogging.LogTrace($"Name: {maps.mapData.MapName}, workshop: {maps.mapData.isWorkshopMap}");
        }

        foreach(CCSPlayerController client in Utilities.GetPlayers()) {
            if(!client.IsValid || client.IsBot || client.IsHLTV)
                continue;
            
            ShowVotingMenu(client);
        }

        voteTimer = plugin.AddTimer(1.0F, () => {
            if(Server.EngineTime - voteStartTime < TEMP_CVAR_VALUE_VOTE_TIME) {
                return;
            }

            EndVote();
        }, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
    }

    public void EndVote() {
        if(voteTimer == null)
            return;
        
        nominationModule.initializeNominations();
        voteTimer.Kill();
        SimpleLogging.LogDebug("Vote ended");
        SimpleLogging.LogTrace("Vote results:");
        foreach(VoteState maps in votingMaps) {
            SimpleLogging.LogTrace($"Votes: {maps.GetVoteCounts()}, Name: {maps.mapData.MapName}, workshop: {maps.mapData.isWorkshopMap}");
        }

        SimpleLogging.LogTrace("Picking winners");
        List<VoteState> winners = PickVoteWinningMaps();

        SimpleLogging.LogTrace($"Winner count: {winners.Count()}");

        foreach(VoteState winMap in winners) {
            SimpleLogging.LogTrace($"Votes: {winMap.GetVoteCounts()}, Name: {winMap.mapData.MapName}, workshop: {winMap.mapData.isWorkshopMap}");
        }

        int votes = 0;
        foreach(VoteState maps in votingMaps) {
            votes += maps.GetVoteCounts();
        }

        if(isActivatedByRTV && votes == 0) {
            SimpleLogging.LogDebug("There is no votes. Extending timelimit...");
            Server.PrintToChatAll($"{plugin.CHAT_PREFIX} There is no votes. Extending timelimit...");
            isVoteInProgress = false;
            plugin.ExtendCurrentMap(15);
            return;
        }

        if(!isRunoffVoteTriggered && winners.Count > 1) {
            SimpleLogging.LogDebug("No map got over X% votes, starting runoff vote");
            Server.PrintToChatAll($"{plugin.CHAT_PREFIX} No map got over X% votes, starting runoff vote");
            runoffVoteMaps = winners;
            StartVoteProcess();
            isRunoffVoteTriggered = true;
            return;
        }

        int totalVotes = 0;

        foreach(VoteState map in votingMaps) {
            totalVotes += map.GetVoteCounts();
        }

        if(winners.First().mapData.MapName.Equals(TEMP_VOTE_MAP_DONT_CHANGE, StringComparison.OrdinalIgnoreCase) && totalVotes != 0) {
            SimpleLogging.LogDebug("Players chose don't change. Waiting for next map vote");
            Server.PrintToChatAll($"{plugin.CHAT_PREFIX} Voting finished.");
            Server.PrintToChatAll($"{plugin.CHAT_PREFIX} Map will not change ({winners.First().GetVoteCounts()} votes of {totalVotes} total votes)");
            isVoteInProgress = false;
            return;
        }
        else if(winners.First().mapData.MapName.Equals(TEMP_VOTE_MAP_EXTEND_MAP, StringComparison.OrdinalIgnoreCase) && totalVotes != 0) {
            SimpleLogging.LogDebug("Players chose extend map");
            Server.PrintToChatAll($"{plugin.CHAT_PREFIX} Voting finished.");
            Server.PrintToChatAll($"{plugin.CHAT_PREFIX} Extending Current Map ({winners.First().GetVoteCounts()} votes of {totalVotes} total votes)");
            plugin.ExtendCurrentMap(15);
            isVoteInProgress = false;
            return;
        }

        nextMap = winners.First().mapData;
        SimpleLogging.LogDebug($"Winner: {nextMap.MapName}");

        Server.PrintToChatAll($"{plugin.CHAT_PREFIX} Voting finished.");
        Server.PrintToChatAll($"{plugin.CHAT_PREFIX} Next map: {nextMap.MapName} ({winners.First().GetVoteCounts()} votes of {totalVotes} total votes)");

        isVoteInProgress = false;
        if(!isActivatedByRTV)
            return;

        // TODO: Implement fake ConVar to specify the map changing timing.
        // After x seconds or After round end.
        if(PluginSettings.GetInstance().cssmcRTVMapChangingAfterRoundEnd.Value) {
            // TODO: Implement map change logic in EventRoundEnd
            shouldRestartAfterRoundEnd = true;
        }
        else {
            plugin.AddTimer(PluginSettings.GetInstance().cssmcRTVMapChangingDelay.Value, () => {
                plugin.ChangeToNextMap(nextMap);
            }, TimerFlags.STOP_ON_MAPCHANGE);
        }
    }

    public void CancelVote(CCSPlayerController? client) {
        if(!isVoteInProgress) 
            return;

        SimpleLogging.LogDebug("Cancelling the vote");

        if(client != null) 
            plugin.Logger.LogInformation($"Admin {client.PlayerName} cancelled the current vote");

        Server.PrintToChatAll($"{plugin.CHAT_PREFIX} Admin cancelled the current vote");
        isVoteInProgress = false;
        voteTimer?.Kill();
        countdownTimer?.Kill();
        nominationModule.initializeNominations();
        SimpleLogging.LogDebug("Vote cancelled");
    }

    private List<VoteState> PickVoteWinningMaps() {
        List<VoteState> winners = new ();

        VoteState? previousMap = null;
        foreach(VoteState votedMap in votingMaps) {
            if(previousMap == null) {
                winners = [votedMap];
            }
            else if(winners.First().GetVoteCounts() < votedMap.GetVoteCounts()) {
                winners = [votedMap];
            }
            else if(winners.First().GetVoteCounts() == votedMap.GetVoteCounts()) {
                winners.Add(votedMap);
            }
            previousMap = votedMap;
        }

        return winners;
    }

    public void ShowVotingMenu(CCSPlayerController client) {
        CenterHtmlMenu menu = new CenterHtmlMenu("MapVote", plugin);

        if(nextMap != null || !isVoteInProgress)
            return;

        foreach(VoteState maps in votingMaps) {
            menu.AddMenuOption(maps.mapData.MapName, (controller, option) => {
                ProcessPlayerVote(controller, option.Text);
            });
        }

        MenuManager.OpenCenterHtmlMenu(plugin, client, menu);
    }

    private void ProcessPlayerVote(CCSPlayerController client, string mapName) {
        SimpleLogging.LogDebug("Start processing the player vote");
        MenuManager.CloseActiveMenu(client);

        VoteState? existingPlayerVote = null;
        VoteState? votingTarget = null;

        SimpleLogging.LogDebug("Iterating voting maps");
        foreach(VoteState map in votingMaps) {
            if(map.GetVotedPlayers().Contains(client))
                existingPlayerVote = map;
            
            if(map.mapData.MapName.Equals(mapName, StringComparison.OrdinalIgnoreCase))
                votingTarget = map;
        }

        if(votingTarget == null) {
            SimpleLogging.LogDebug($"Specified map {mapName} is not exists in current vote");
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


        int totalVotes = 0;

        foreach(VoteState map in votingMaps) {
            totalVotes += map.GetVoteCounts();
        }

        int totalHumanPlayers = 0;
        foreach(CCSPlayerController cl in Utilities.GetPlayers()) {
            if(!cl.IsValid || cl.IsBot || cl.IsHLTV)
                continue;
            
            totalHumanPlayers++;
        }

        if(totalVotes >= totalHumanPlayers) {
            EndVote();
        }
    }

    public bool IsVoteInProgress() {
        return isVoteInProgress;
    }

    public bool ShouldRestartAfterRoundEnd() {
        return shouldRestartAfterRoundEnd;
    }

    public MapData? GetNextMap() {
        return nextMap;
    }
}