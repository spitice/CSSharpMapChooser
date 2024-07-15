using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
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


    private List<NominationData> nominatedMaps;
    private List<MapData> mapList;

    private List<VoteState> votingMaps = new ();
    List<VoteState>? runoffVoteMaps = null;

    private MapData? nextMap = null;

    private Timer? voteTimer = null;
    Timer? countdownTimer = null;
    private Random random = new ();

    private const float TEMP_CVAR_VALUE_COUNTDOWN_TIME = 15.0F;
    private const bool TEMP_CVAR_VALUE_ROUND_END = true;
    private const float TEMP_CVAR_VALUE_MAP_CHANGING_DELAY = 10.0F;


    public VoteManager(List<NominationData> nominatedMaps, List<MapData> mapList, CSSMapChooser plugin, bool isActivatedByRTV = false) {
        this.nominatedMaps = nominatedMaps;
        this.mapList = mapList;
        this.plugin = plugin;
        this.isActivatedByRTV = isActivatedByRTV;
    }

    public void StartVoteProcess() {
        isVoteInProgress = true;
        
        double countdownStartTime = Server.EngineTime;

        SimpleLogging.LogDebug($"Initiating vote countdown. seconds: {TEMP_CVAR_VALUE_COUNTDOWN_TIME}");
        Server.PrintToChatAll($"{plugin.CHAT_PREFIX} Vote starting in {TEMP_CVAR_VALUE_COUNTDOWN_TIME} seconds");

        countdownTimer = plugin.AddTimer(1.0F, () => {
            var elapsedTime = Server.EngineTime - countdownStartTime;
            var remainingTime = TEMP_CVAR_VALUE_COUNTDOWN_TIME - elapsedTime;

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

        if(runoffVoteMaps == null) {
            SimpleLogging.LogDebug("This is a initial vote");
            foreach(NominationData nominated in nominatedMaps) {
                votingMaps.Add(new VoteState(nominated.mapData));
            }

            for(int i = votingMaps.Count(); i < 9; i++) {
                int index = random.Next(mapList.Count());
                votingMaps.Add(new VoteState(mapList[index]));
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


        nextMap = winners.First().mapData;
        SimpleLogging.LogDebug($"Winner: {nextMap.MapName}");

        isVoteInProgress = false;
        if(!isActivatedByRTV)
            return;

        // TODO: Implement fake ConVar to specify the map changing timing.
        // After x seconds or After round end.
        if(TEMP_CVAR_VALUE_ROUND_END) {
            // TODO: Implement map change logic in EventRoundEnd
            shouldRestartAfterRoundEnd = true;
        }
        else {
            plugin.AddTimer(TEMP_CVAR_VALUE_MAP_CHANGING_DELAY, () => {
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