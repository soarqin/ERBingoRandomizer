namespace ERBingoRandomizer.Randomizer;

public class SeedInfo {
    public SeedInfo(string seed, string sha256Hash, bool randomStartupClasses = true, bool randomWeapons = true, bool openGraces = false, bool reduceUpgradeMat = false, int reduceUpgradeMatType = 1, bool greaterItemLootChance = false) {
        Seed = seed;
        Sha256Hash = sha256Hash;
        RandomStartupClasses = randomStartupClasses;
        RandomWeapons = randomWeapons;
        OpenGraces = openGraces;
        ReduceUpgradeMat = reduceUpgradeMat;
        ReduceUpgradeMatType = reduceUpgradeMatType;
        GreaterItemLootChance = greaterItemLootChance;
    }
    public string Seed { get; }
    public string Sha256Hash { get; }
    public bool RandomStartupClasses { get; }
    public bool RandomWeapons { get; }
    public bool OpenGraces { get; }
    public bool ReduceUpgradeMat { get; }
    public int ReduceUpgradeMatType { get; }
    public bool GreaterItemLootChance { get; }
}
