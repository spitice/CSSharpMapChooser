using CounterStrikeSharp.API.Core;

namespace CSSMapChooser;

public class NominationData {

    public readonly MapData mapData;

    private readonly List<CCSPlayerController> nominators = new();

    public readonly bool isForceNominate;

    public NominationData(MapData mapData, bool isForceNominate = false) {
        this.mapData = mapData;
        this.isForceNominate = isForceNominate;
    }

    public void AddNominator(CCSPlayerController nominator) {
        if(nominators.Contains(nominator))
            throw new ArgumentException("Duplicated nominator specified!");

        nominators.Add(nominator);
    }

    public void RemoveNominator(CCSPlayerController nominator) {
        nominators.Remove(nominator);
    }

    public List<CCSPlayerController> GetNominators() {
        return nominators;
    }
}