using CounterStrikeSharp.API.Core;

namespace CSSMapChooser;

public class VoteState {

    public readonly MapData mapData;

    private List<CCSPlayerController> playerVotes = new ();

    public VoteState(MapData mapData) {
        this.mapData = mapData;
    }

    public void AddVotedPlayer(CCSPlayerController player) {
        playerVotes.Add(player);
    }

    public void RemoveVotedPlayer(CCSPlayerController player) {
        playerVotes.Remove(player);
    }

    public List<CCSPlayerController> GetVotedPlayers() {
        return new List<CCSPlayerController>(playerVotes);
    }

    public void ResetVotes() {
        playerVotes.Clear();
    }

    public int GetVoteCounts() {
        return playerVotes.Count();
    }
}