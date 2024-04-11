namespace ERBingoRandomizer.Randomizer;

public class SeedInfo {
    public SeedInfo(string seed, string sha256Hash, bool randomStartupClasses = true, bool randomWeapons = true, bool openGraces = true, bool reduceUpgradeMat = true) {
        Seed = seed;
        RandomStartupClasses = randomStartupClasses;
        RandomWeapons = randomWeapons;
        OpenGraces = openGraces;
        ReduceUpgradeMat = reduceUpgradeMat;
        Sha256Hash = sha256Hash;
    }
    public string Seed { get; }
    public string Sha256Hash { get; }
    public bool RandomStartupClasses { get; }
    public bool RandomWeapons { get; }
    public bool OpenGraces { get; }
    public bool ReduceUpgradeMat { get; }
}
