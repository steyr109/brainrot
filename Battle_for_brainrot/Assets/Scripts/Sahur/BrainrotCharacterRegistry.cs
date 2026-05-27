public struct BrainrotCharacterDefinition
{
    public string displayName;
    public string resourceName;
    public float scale;
    public float spawnY;
    public bool selectable;
    public bool tutorialOnly;

    public BrainrotCharacterDefinition(string displayName, string resourceName, float scale, float spawnY, bool selectable, bool tutorialOnly = false)
    {
        this.displayName = displayName;
        this.resourceName = resourceName;
        this.scale = scale;
        this.spawnY = spawnY;
        this.selectable = selectable;
        this.tutorialOnly = tutorialOnly;
    }
}

public static class BrainrotCharacterRegistry
{
    public const string Sahur = "Sahur";
    public const string BalerinaCapuchino = "BalerinaCapuchino";
    public const string TralaleloTralala = "TralaleloTralala";
    public const string Shoto = "Shoto";

    private static readonly BrainrotCharacterDefinition[] SelectableDefinitions =
    {
        new BrainrotCharacterDefinition("SAHUR", Sahur, 0.27f, -0.55f, true),
        new BrainrotCharacterDefinition("BALERINA CAPUCHINO", BalerinaCapuchino, 0.27f, -0.55f, true),
        new BrainrotCharacterDefinition("TRALALELO TRALALA", TralaleloTralala, 0.27f, -1.10f, true)
    };

    private static readonly BrainrotCharacterDefinition TutorialShotoDefinition =
        new BrainrotCharacterDefinition("SHOTO", Shoto, 0.74f, -0.55f, false, true);

    public static BrainrotCharacterDefinition[] SelectableCharacters => SelectableDefinitions;

    public static BrainrotCharacterDefinition DefaultCharacter => SelectableDefinitions[0];

    public static BrainrotCharacterDefinition TutorialShoto => TutorialShotoDefinition;

    public static BrainrotCharacterDefinition GetByResourceName(string resourceName)
    {
        if (resourceName == Shoto)
            return TutorialShotoDefinition;

        for (int i = 0; i < SelectableDefinitions.Length; i++)
        {
            if (SelectableDefinitions[i].resourceName == resourceName)
                return SelectableDefinitions[i];
        }

        return DefaultCharacter;
    }
}
