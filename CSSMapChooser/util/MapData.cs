namespace CSSMapChooser;

public class MapData {

    public readonly string MapName;

    public readonly bool isWorkshopMap;

    public MapData(string MapName, bool isWorkshopMap) {
        this.MapName = MapName;
        this.isWorkshopMap = isWorkshopMap;
    }
}