using ERBingoRandomizer.Params;
using ERBingoRandomizer.Randomizer.Strategies;
using ERBingoRandomizer.Randomizer.Strategies.CharaInitParam;
using ERBingoRandomizer.Utility;
using FSParam;
using SoulsFormats;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static ERBingoRandomizer.Params.EquipParamWeapon;


namespace ERBingoRandomizer.Randomizer;

public struct RandomizeRule {
    public string Seed;
    public bool RandomStartupClasses;
    public bool RandomWeapons;
    public bool OpenGraces;
    public bool ReduceUpgradeMat;
    public int ReduceUpgradeMatType;
    public KeyValuePair<int, int>[] StartupItems;
}

public partial class BingoRandomizer {
    private RandoResource _resources;
    // Strategies
    private readonly IBingoClassStrategy _classRandomizer;
    private readonly bool _randomStartupClasses;
    private readonly bool _randomWeapons;
    private readonly bool _openGraces;
    private readonly int _reduceUpgradeMatType;
    private readonly KeyValuePair<int, int>[] _startupItems;
    
    //static async method that behaves like a constructor    
    public static async Task<BingoRandomizer> BuildRandomizerAsync(string path, RandomizeRule rule,
        CancellationToken cancellationToken) {
        BingoRandomizer rando = new(path, rule, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Run(() => rando.init(), cancellationToken);
        return rando;
    }
    // Cancellation Token
    private readonly CancellationToken _cancellationToken;
    private BingoRandomizer(string path, RandomizeRule rule, CancellationToken cancellationToken) {
        _resources = new RandoResource(path, rule.Seed, cancellationToken);
        _randomStartupClasses = rule.RandomStartupClasses;
        _randomWeapons = rule.RandomWeapons;
        _openGraces = rule.OpenGraces;
        _reduceUpgradeMatType = rule.ReduceUpgradeMat ? rule.ReduceUpgradeMatType : -1;
        _startupItems = rule.StartupItems;
        _classRandomizer = new Season3ClassRandomizer(new Season2LevelRandomizer(_resources.Random), _resources);
        _cancellationToken = cancellationToken;
    }

    private Task init() {
        return _resources.Init();
    }

    public Task RandomizeRegulation() {
        //calculateLevels();
        if (_randomStartupClasses) {
            _classRandomizer.RandomizeCharaInitParam();
            _cancellationToken.ThrowIfCancellationRequested();
        }
        if (_randomWeapons) {
            randomizeItemLotParams();
            _cancellationToken.ThrowIfCancellationRequested();
            randomizeShopLineupParam();
            _cancellationToken.ThrowIfCancellationRequested();
            randomizeShopLineupParamMagic();
        }
        _cancellationToken.ThrowIfCancellationRequested();
        patchAtkParam();
        _cancellationToken.ThrowIfCancellationRequested();
        if (_reduceUpgradeMatType >= 0)
            changeUpgradeMaterialType(_reduceUpgradeMatType);
        _cancellationToken.ThrowIfCancellationRequested();
        if (_openGraces) {
            unlockSeason3Graces();
            _cancellationToken.ThrowIfCancellationRequested();
        }
        _cancellationToken.ThrowIfCancellationRequested();
        addStartupItems();
        _cancellationToken.ThrowIfCancellationRequested();
        if (_openGraces) {
            increaseItemLotChance();
            _cancellationToken.ThrowIfCancellationRequested();
        }
        writeFiles();
        Logger.WriteLog(_resources.Seed);
        return Task.CompletedTask;
    }

    private void changeUpgradeMaterialType(int type) {
        foreach (Param.Row row in _resources.EquipMtrlSetParam.Rows) {
            EquipMtrlSetParam mtrl = new EquipMtrlSetParam(row);
            
            int id = mtrl.materialId01;
            int cat = mtrl.materialCate01;
            int num = mtrl.itemNum01;
            if (cat == 4 && id >= 10100 && id < 10110 && num > 1) {
                if (type == 0)
                {
                    mtrl.itemNum01 = 1;
                }
                else
                {
                    mtrl.itemNum01 = (sbyte)(num >> 1);
                }
            }
        }
    }

    public string GetSeed() {
        return _resources.Seed;
    }

    private void addItemWithLotID(int id, int itemId, int itemCat, int itemCount)
    {
        Param.Row row = new Param.Row(id, "", _resources.ItemLotParamMap);
        row.Cells.FirstOrDefault(c => c.Def.InternalName == "lotItemId01").SetValue(itemId);
        row.Cells.FirstOrDefault(c => c.Def.InternalName == "lotItemCategory01").SetValue(itemCat);
        row.Cells.FirstOrDefault(c => c.Def.InternalName == "lotItemBasePoint01").SetValue((ushort)100);
        row.Cells.FirstOrDefault(c => c.Def.InternalName == "lotItemNum01").SetValue((byte)itemCount);
        _resources.ItemLotParamMap.AddRow(row);
    }

    private void addStartupItems()
    {
        if (_startupItems.Length == 0) return;

        var common = _resources.CommonEmevd;
        if (common == null) {
            throw new InvalidOperationException($"Missing emevd {Const.CommonEventPath}");
        }
        var newEventId = 279551112;  // Arbitrary number
        List<EMEVD.Instruction> newInstrs =
        [
            new EMEVD.Instruction(1003, 2, new List<object> { (byte)0, (byte)1, (byte)0, (uint)60000 }),
            // IfEventFlag(MAIN, ON, TargetEventFlagType.EventFlag, 60000)
            new EMEVD.Instruction(3, 0, new List<object> { (sbyte)0, (byte)1, (byte)0, (uint)60000 })
        ];
        for (var i = 0; i < _startupItems.Length; i++)
        {
            var (id, cat) = _startupItems[i];
            addItemWithLotID(70001 + i, id, cat, 1);
            if (i % 10 == 0)
            {
                // AwardItemLot(70001 + i)
                newInstrs.Add(new EMEVD.Instruction(2003, 4, new List<object> { 70001 + i }));
            }
        }
        // EndEvent()
        newInstrs.Add(new EMEVD.Instruction(1000, 4, new List<object> { (byte)0 }));
        var newEvent = new EMEVD.Event(newEventId, EMEVD.Event.RestBehaviorType.Default)
        {
            Instructions = newInstrs
        };
        common.Events.Add(newEvent);
        var constrEvent = common.Events.Find(e => e.ID == 0);
        if (constrEvent == null) {
            throw new InvalidOperationException($"{Const.CommonEventPath} missing of required event: 0");
        }
        // Initialize new event
        constrEvent.Instructions.Add(new EMEVD.Instruction(2000, 0, new List<object> { 0, newEventId, 0 }));
    }

    private void increaseItemLotChance()
    {
        var cols = _resources.ItemLotParamEnemy.Columns;
        Param.Column? itemId1Col = null;
        Param.Column? itemId2Col = null;
        Param.Column? itemId3Col = null;
        Param.Column? itemCat1Col = null;
        Param.Column? itemCat2Col = null;
        Param.Column? itemBasePoint1Col = null;
        Param.Column? itemBasePoint2Col = null;
        foreach (var c in cols)
        {
            switch (c.Def.InternalName)
            {
                case "lotItemId01":
                    itemId1Col = c;
                    break;
                case "lotItemId02":
                    itemId2Col = c;
                    break;
                case "lotItemId03":
                    itemId3Col = c;
                    break;
                case "lotItemCategory01":
                    itemCat1Col = c;
                    break;
                case "lotItemCategory02":
                    itemCat2Col = c;
                    break;
                case "lotItemBasePoint01":
                    itemBasePoint1Col = c;
                    break;
                case "lotItemBasePoint02":
                    itemBasePoint2Col = c;
                    break;
            }
        }
        if (itemId1Col == null || itemId2Col == null || itemId3Col == null || itemCat1Col == null || itemCat2Col == null || itemBasePoint1Col == null || itemBasePoint2Col == null) return;
        foreach (Param.Row row in _resources.ItemLotParamEnemy.Rows)
        {
            if ((int)itemId3Col.GetValue(row) > 0)
            {
                continue;
            }

            var itemId1 = (int)itemId1Col.GetValue(row);
            var itemId2 = (int)itemId2Col.GetValue(row);
            if (itemId1 <= 0 && itemId2 > 0 && (int)itemCat2Col.GetValue(row) is 2 or 3)
            {
                var chance = (ushort)itemBasePoint2Col.GetValue(row);
                if (chance < 500)
                {
                    itemBasePoint1Col.SetValue(row, (ushort)500);
                    itemBasePoint2Col.SetValue(row, (ushort)500);
                }
            }
            else if (itemId1 > 0 && itemId2 <= 0 && (int)itemCat1Col.GetValue(row) is 2 or 3)
            {
                var chance = (ushort)itemBasePoint1Col.GetValue(row);
                if (chance < 500)
                {
                    itemBasePoint1Col.SetValue(row, (ushort)500);
                    itemBasePoint2Col.SetValue(row, (ushort)500);
                }
            }
        }
    }

    private void randomizeItemLotParams() {
        OrderedDictionary categoryDictEnemy = new();
        OrderedDictionary categoryDictMap = new();

        IEnumerable<Param.Row> itemLotParamMap =
            _resources.ItemLotParamMap.Rows.Where(id => !Unk.unkItemLotParamMapWeapons.Contains(id.ID));
        IEnumerable<Param.Row> itemLotParamEnemy =
            _resources.ItemLotParamEnemy.Rows.Where(id => !Unk.unkItemLotParamEnemyWeapons.Contains(id.ID));

        foreach (Param.Row row in itemLotParamEnemy.Concat(itemLotParamMap)) {
            Param.Column[] itemIds = row.Columns.Take(Const.ItemLots).ToArray();
            Param.Column[] categories = row.Columns.Skip(Const.CategoriesStart).Take(Const.ItemLots).ToArray();
            Param.Column[] chances = row.Columns.Skip(Const.ChanceStart).Take(Const.ItemLots).ToArray();
            int totalWeight = chances.Sum(a => (ushort)a.GetValue(row));
            for (int i = 0; i < Const.ItemLots; i++) {
                int category = (int)categories[i].GetValue(row);
                if (category != Const.ItemLotWeaponCategory && category != Const.ItemLotCustomWeaponCategory) {
                    continue;
                }

                int id = (int)itemIds[i].GetValue(row);
                int sanitizedId = RemoveWeaponLevels(id);
                if (category == Const.ItemLotWeaponCategory) {
                    if (!_resources.WeaponDictionary.TryGetValue(sanitizedId, out EquipParamWeapon? wep)) {
                        continue;
                    }

                    if (wep.wepType is WeaponType.GlintstoneStaff or WeaponType.FingerSeal) {
                        continue;
                    }

                    if (id != sanitizedId) {
                        int difference = id - sanitizedId;
                        string differenceString = difference != 0 ? $" +{difference}" : string.Empty;
                        _resources.WeaponNameDictionary[id] =
                            $"{_resources.WeaponNameDictionary[sanitizedId]}{differenceString}";
                    }

                    ushort chance = (ushort)chances[i].GetValue(row);
                    if (chance == totalWeight) {
                        addToOrderedDict(categoryDictMap, wep.wepType, new ItemLotEntry(id, category));
                        break; // Break here because the entire item lot param is just a single entry.
                    }

                    addToOrderedDict(categoryDictEnemy, wep.wepType, new ItemLotEntry(id, category));
                }
                else {
                    // category == Const.ItemLotCustomWeaponCategory
                    if (!_resources.CustomWeaponDictionary.TryGetValue(id, out EquipParamWeapon? wep)) {
                        continue;
                    }

                    if (wep.wepType is WeaponType.GlintstoneStaff or WeaponType.FingerSeal) {
                        continue;
                    }

                    Param.Row paramRow = _resources.EquipParamCustomWeapon[id]!;
                    EquipParamCustomWeapon customWeapon = new(paramRow);
                    if (!_resources.WeaponNameDictionary.ContainsKey(customWeapon.baseWepId)) {
                        int baseWeaponId = customWeapon.baseWepId;
                        int customSanitizedId = RemoveWeaponLevels(baseWeaponId);
                        int difference = baseWeaponId - customSanitizedId;
                        string differenceString = difference != 0 ? $" +{difference}" : string.Empty;
                        _resources.WeaponNameDictionary[id] =
                            $"{_resources.WeaponNameDictionary[baseWeaponId]}{differenceString}";
                    }

                    ushort chance = (ushort)chances[i].GetValue(row);
                    if (chance == totalWeight) {
                        addToOrderedDict(categoryDictMap, wep.wepType, new ItemLotEntry(id, category));
                        break;
                    }

                    addToOrderedDict(categoryDictEnemy, wep.wepType, new ItemLotEntry(id, category));
                }
            }
        }

        foreach (Param.Row row in _resources.ShopLineupParam.Rows) {
            if ((byte)row["equipType"]!.Value.Value != Const.ShopLineupWeaponCategory || row.ID >= 101900) {
                continue;
            }

            ShopLineupParam lot = new(new Param.Row(row));
            int sanitizedId = RemoveWeaponLevels(lot.equipId);
            if (!_resources.WeaponDictionary.TryGetValue(sanitizedId, out EquipParamWeapon? wep)) {
                continue;
            }

            if (wep.wepType is WeaponType.GlintstoneStaff or WeaponType.FingerSeal) {
                continue;
            }

            // if (lot.equipId != sanitizedId) {
            //     _resources.WeaponNameDictionary[lot.equipId] = $"{_resources.WeaponNameDictionary[sanitizedId]} +{lot.equipId - sanitizedId}";
            // }
            addToOrderedDict(categoryDictMap, wep.wepType, new ItemLotEntry(lot.equipId, 2));
        }

        dedupeAndRandomizeVectors(categoryDictMap);
        dedupeAndRandomizeVectors(categoryDictEnemy);

        //Console.WriteLine("categoryDictMap");
        //dumpCategoriesAndCounts(categoryDictMap);
        //dumpCategoriesAndNames(categoryDictMap);
        Console.WriteLine("categoryDictEnemy");
        dumpCategoriesAndNames(categoryDictMap);

        Dictionary<int, ItemLotEntry> guaranteedDropReplace = getReplacementHashmap(categoryDictMap);
        Dictionary<int, ItemLotEntry> chanceDropReplace = getReplacementHashmap(categoryDictEnemy);
        Logger.LogItem(">> Item Replacements - all instances of item on left will be replaced with item on right");
        Logger.LogItem("> Guaranteed Weapons");
        logReplacementDictionary(guaranteedDropReplace);
        Logger.LogItem("> Chance Weapons");
        logReplacementDictionary(chanceDropReplace);
        Logger.LogItem("");

        foreach (Param.Row row in _resources.ItemLotParamEnemy.Rows.Concat(_resources.ItemLotParamMap.Rows)) {
            Param.Column[] itemIds = row.Columns.Take(Const.ItemLots).ToArray();
            Param.Column[] categories = row.Columns.Skip(Const.CategoriesStart).Take(Const.ItemLots).ToArray();
            for (int i = 0; i < Const.ItemLots; i++) {
                int category = (int)categories[i].GetValue(row);
                if (category != Const.ItemLotWeaponCategory && category != Const.ItemLotCustomWeaponCategory) {
                    continue;
                }

                int id = (int)itemIds[i].GetValue(row);
                if (category == Const.ItemLotWeaponCategory) {
                    if (!_resources.WeaponDictionary.TryGetValue(RemoveWeaponLevels(id), out _)) {
                        continue;
                    }

                    if (guaranteedDropReplace.TryGetValue(id, out ItemLotEntry entry)) {
                        itemIds[i].SetValue(row, entry.Id);
                        categories[i].SetValue(row, entry.Category);
                        break;
                    }
                    if (!chanceDropReplace.TryGetValue(id, out entry)) {
                        continue;
                    }
                    itemIds[i].SetValue(row, entry.Id);
                    categories[i].SetValue(row, entry.Category);
                }
                else {
                    // category == Const.ItemLotCustomWeaponCategory
                    if (!_resources.CustomWeaponDictionary.TryGetValue(id, out _)) {
                        continue;
                    }

                    if (guaranteedDropReplace.TryGetValue(id, out ItemLotEntry entry)) {
                        itemIds[i].SetValue(row, entry.Id);
                        categories[i].SetValue(row, entry.Category);
                    }
                    if (!chanceDropReplace.TryGetValue(id, out entry)) {
                        continue;
                    }
                    itemIds[i].SetValue(row, entry.Id);
                    categories[i].SetValue(row, entry.Category);
                }
            }
        }
    }
    private void dumpCategoriesAndNames(OrderedDictionary dictionary) {
        foreach (object? key in dictionary.Keys) {
            List<ItemLotEntry> list = (List<ItemLotEntry>)dictionary[key]!;
            EquipParamWeapon.WeaponType type = (EquipParamWeapon.WeaponType)key;
            Console.WriteLine($"{type}");
            foreach (ItemLotEntry itemLotEntry in list) {
                int id = RemoveWeaponLevels(itemLotEntry.Id);
                string name = _resources.WeaponFmg[0][id];
                if (string.IsNullOrWhiteSpace(name)) {
                    name = $"{_resources.WeaponNameDictionary[itemLotEntry.Id]}";
                }
                Console.WriteLine($"\t{name}");
            }
        }
    }
    private void dumpCategoriesAndCounts(OrderedDictionary dictionary) {
        foreach (object? key in dictionary.Keys) {
            List<ItemLotEntry> list = (List<ItemLotEntry>)dictionary[key]!;
            EquipParamWeapon.WeaponType type = (EquipParamWeapon.WeaponType)key;
            Console.WriteLine($"{type} = {list.Count}");
        }
    }
    private void randomizeShopLineupParam() {
        List<ShopLineupParam> shopLineupParamRemembranceList = new();
        foreach (Param.Row row in _resources.ShopLineupParam.Rows) {
            if ((byte)row["equipType"]!.Value.Value != Const.ShopLineupWeaponCategory ||
                (row.ID < 101900 || row.ID > 101980)) {
                continue;
            }

            ShopLineupParam lot = new(new Param.Row(row));
            int sanitizedId = RemoveWeaponLevels(lot.equipId);
            if (!_resources.WeaponDictionary.TryGetValue(sanitizedId, out _)) {
                continue;
            }

            // if (lot.equipId != sanitizedId) {
            //     _resources.WeaponNameDictionary[lot.equipId] = $"{_resources.WeaponNameDictionary[sanitizedId]} +{lot.equipId - sanitizedId}";
            // }
            shopLineupParamRemembranceList.Add(lot);
        }

        List<Param.Row> staves = _resources.WeaponTypeDictionary[WeaponType.GlintstoneStaff];
        List<Param.Row> seals = _resources.WeaponTypeDictionary[WeaponType.FingerSeal];
        List<int> shopLineupParamList = _resources.WeaponDictionary.Keys.Select(RemoveWeaponMetadata).Distinct()
            .Where(i => shopLineupParamRemembranceList.All(s => s.equipId != i))
            .Where(id => staves.All(s => s.ID != id) && seals.All(s => s.ID != id))
            .ToList();
        shopLineupParamList.Shuffle(_resources.Random);
        shopLineupParamRemembranceList.Shuffle(_resources.Random);

        Logger.LogItem(
            ">> Shop Replacements - Random item selected from pool of all weapons (not including infused weapons). Remembrances are randomized amongst each-other.");

        foreach (Param.Row row in _resources.ShopLineupParam.Rows) {
            logShopId(row.ID);
            if ((byte)row["equipType"]!.Value.Value != Const.ShopLineupWeaponCategory || row.ID > 101980) {
                continue;
            }

            ShopLineupParam lot = new(row);
            if (!_resources.WeaponDictionary.TryGetValue(RemoveWeaponLevels(lot.equipId), out EquipParamWeapon? wep)) {
                continue;
            }

            if (wep.wepType is WeaponType.GlintstoneStaff or WeaponType.FingerSeal) {
                continue;
            }

            replaceShopLineupParam(lot, shopLineupParamList, shopLineupParamRemembranceList);
        }
    }
    private void randomizeShopLineupParamMagic() {
        OrderedDictionary magicCategoryDictMap = new();
        List<ShopLineupParam> shopLineupParamRemembranceList = new();
        List<ShopLineupParam> shopLineupParamDragonList = new();
        foreach (Param.Row row in _resources.ShopLineupParam.Rows) {
            if ((byte)row["equipType"]!.Value.Value != Const.ShopLineupGoodsCategory || row.ID > 101980) {
                continue;
            }

            ShopLineupParam lot = new(new Param.Row(row));
            if (!_resources.MagicDictionary.TryGetValue(lot.equipId, out Magic? magic)) {
                continue;
            }
            if (row.ID < 101950) {
                if (lot.mtrlId == -1) {
                    addToOrderedDict(magicCategoryDictMap, magic.ezStateBehaviorType, lot.equipId);
                    continue;
                }
                shopLineupParamRemembranceList.Add(lot);
            }
            else {
                // Dragon Communion Shop 101950 - 101980 
                shopLineupParamDragonList.Add(lot);
            }
        }

        foreach (Param.Row row in _resources.ItemLotParamEnemy.Rows.Concat(_resources.ItemLotParamMap.Rows)) {
            Param.Column[] itemIds = row.Columns.Take(Const.ItemLots).ToArray();
            Param.Column[] categories = row.Columns.Skip(Const.CategoriesStart).Take(Const.ItemLots).ToArray();
            Param.Column[] chances = row.Columns.Skip(Const.ChanceStart).Take(Const.ItemLots).ToArray();
            int totalWeight = chances.Sum(a => (ushort)a.GetValue(row));
            for (int i = 0; i < Const.ItemLots; i++) {
                int category = (int)categories[i].GetValue(row);
                if (category != Const.ItemLotGoodsCategory) {
                    continue;
                }

                int id = (int)itemIds[i].GetValue(row);
                if (!_resources.MagicDictionary.TryGetValue(id, out Magic? magic)) {
                    continue;
                }
                ushort chance = (ushort)chances[i].GetValue(row);
                if (chance == totalWeight) {
                    addToOrderedDict(magicCategoryDictMap, magic.ezStateBehaviorType, id);
                    break;
                }
                addToOrderedDict(magicCategoryDictMap, magic.ezStateBehaviorType, id);
            }
        }

        dedupeAndRandomizeShopVectors(magicCategoryDictMap);

        Dictionary<int, int> magicShopReplacement = getShopReplacementHashmap(magicCategoryDictMap);
        shopLineupParamRemembranceList.Shuffle(_resources.Random);
        shopLineupParamDragonList.Shuffle(_resources.Random);
        Logger.LogItem("\n>> All Magic Replacement.");
        logReplacementDictionaryMagic(magicShopReplacement);

        Logger.LogItem("\n>> Shop Magic Replacement.");
        foreach (Param.Row row in _resources.ShopLineupParam.Rows) {
            logShopIdMagic(row.ID);
            if ((byte)row["equipType"]!.Value.Value != Const.ShopLineupGoodsCategory || row.ID > 101980) {
                continue;
            }

            ShopLineupParam lot = new(row);
            if (!_resources.MagicDictionary.TryGetValue(lot.equipId, out _)) {
                continue;
            }
            if (row.ID < 101950) {
                replaceShopLineupParamMagic(lot, magicShopReplacement, shopLineupParamRemembranceList);
            }
            else {
                ShopLineupParam newDragonIncant = getNewId(lot.equipId, shopLineupParamDragonList);
                Logger.LogItem($"{_resources.GoodsFmg[0][lot.equipId]} -> {_resources.GoodsFmg[0][newDragonIncant.equipId]}");
                copyShopLineupParam(lot, newDragonIncant);
            }
        }

        foreach (Param.Row row in _resources.ItemLotParamEnemy.Rows.Concat(_resources.ItemLotParamMap.Rows)) {
            Param.Column[] itemIds = row.Columns.Take(Const.ItemLots).ToArray();
            Param.Column[] categories = row.Columns.Skip(Const.CategoriesStart).Take(Const.ItemLots).ToArray();
            for (int i = 0; i < Const.ItemLots; i++) {
                int category = (int)categories[i].GetValue(row);
                if (category != Const.ItemLotGoodsCategory) {
                    continue;
                }

                int id = (int)itemIds[i].GetValue(row);
                if (!_resources.MagicDictionary.TryGetValue(id, out Magic _)) {
                    continue;
                }

                if (!magicShopReplacement.TryGetValue(id, out int entry)) {
                    continue;
                }
                itemIds[i].SetValue(row, entry);
            }
        }
    }
    private void unlockSeason3Graces() {
        // There are a few parts to this:
        // - Set many flags when Torrent is unlocked. Do this every time we load in, especially if the mod gets updated or wasn't installed right.
        // - To avoid map notification spam, don't set the event flag which notifies the in-game map to do the notification.
        // - Adjust logic for Altus detection event so the custom bonfires don't count for it.

        int altusId = 76303; // Altus Highway Junction
        int gelmirId = 76353; // Road of Iniquity
        List<int> bonfireFlags = new() {
            71190, // Roundtable Hold

            // 76101, // Limgrave - Limgrave - The First Step
            // 76100, // Limgrave - Limgrave - Church of Elleh
            // 76111, // Limgrave - Limgrave - Gatefront
            // 76120, // Limgrave - Limgrave - Waypoint Ruins Cellar
            // 76103, // Limgrave - Limgrave - Artist's Shack (Limgrave)
            // 76104, // Limgrave - Limgrave - Third Church of Marika
            // 76105, // Limgrave - Limgrave - Fort Haight West
            // 76106, // Limgrave - Limgrave - Agheel Lake South
            // 76108, // Limgrave - Limgrave - Agheel Lake North
            // 76110, // Limgrave - Limgrave - Church of Dragon Communion
            // 76113, // Limgrave - Limgrave - Seaside Ruins
            // 76114, // Limgrave - Limgrave - Mistwood Outskirts
            // 76116, // Limgrave - Limgrave - Murkwater Coast
            // 76119, // Limgrave - Limgrave - Summonwater Village Outskirts
            // 73002, // Limgrave - Limgrave - Stormfoot Catacombs
            // 73004, // Limgrave - Limgrave - Murkwater Catacombs
            // 73103, // Limgrave - Limgrave - Groveside Cave
            // 73115, // Limgrave - Limgrave - Coastal Cave
            // 73100, // Limgrave - Limgrave - Murkwater Cave
            // 73117, // Limgrave - Limgrave - Highroad Cave
            // 73201, // Limgrave - Limgrave - Limgrave Tunnels
            // 71800, // Limgrave - Stranded Graveyard - Cave of Knowledge
            // 71801, // Limgrave - Stranded Graveyard - Stranded Graveyard
            // 76102, // Limgrave - Stormhill - Stormhill Shack
            // 71002, // Limgrave - Stormhill - Castleward Tunnel
            // 76118, // Limgrave - Stormhill - Warmaster's Shack
            // 76117, // Limgrave - Stormhill - Saintsbridge
            // 73011, // Limgrave - Stormhill - Deathtouched Catacombs
            // 73410, // Limgrave - Stormhill - Limgrave Tower Bridge
            // 73412, // Limgrave - Stormhill - Divine Tower of Limgrave
            // 76150, // Limgrave - Weeping Peninsula - Church of Pilgrimage
            // 76151, // Limgrave - Weeping Peninsula - Castle Morne Rampart
            // 76152, // Limgrave - Weeping Peninsula - Tombsward
            // 76153, // Limgrave - Weeping Peninsula - South of the Lookout Tower
            // 76154, // Limgrave - Weeping Peninsula - Ailing Village Outskirts
            // 76155, // Limgrave - Weeping Peninsula - Beside the Crater-Pocked Glade
            // 76156, // Limgrave - Weeping Peninsula - Isolated Merchant's Shack (Limgrave)
            // 76162, // Limgrave - Weeping Peninsula - Fourth Church of Marika
            // 76157, // Limgrave - Weeping Peninsula - Bridge of Sacrifice
            // 76158, // Limgrave - Weeping Peninsula - Castle Morne Lift
            // 76159, // Limgrave - Weeping Peninsula - Behind The Castle
            // 76160, // Limgrave - Weeping Peninsula - Beside the Rampart Gaol
            // 73001, // Limgrave - Weeping Peninsula - Impaler's Catacombs
            // 73000, // Limgrave - Weeping Peninsula - Tombsward Catacombs
            // 73101, // Limgrave - Weeping Peninsula - Earthbore Cave
            // 73102, // Limgrave - Weeping Peninsula - Tombsward Cave
            // 73200, // Limgrave - Weeping Peninsula - Morne Tunnel
            // 71008, // Stormveil Castle - Stormveil Main Gate
            // 71003, // Stormveil Castle - Gateside Chamber
            // 71004, // Stormveil Castle - Stormveil Cliffside
            // 71005, // Stormveil Castle - Rampart Tower
            // 71006, // Stormveil Castle - Liftside Chamber
            // 71007, // Stormveil Castle - Secluded Cell
            // 76200, // Liurnia of the Lakes - Liurnia of the Lakes - Lake-Facing Cliffs
            // 76202, // Liurnia of the Lakes - Liurnia of the Lakes - Laskyar Ruins
            // 76201, // Liurnia of the Lakes - Liurnia of the Lakes - Liurnia Lake Shore
            // 76204, // Liurnia of the Lakes - Liurnia of the Lakes - Academy Gate Town
            // 76217, // Liurnia of the Lakes - Liurnia of the Lakes - Artist's Shack (Liurnia of the Lakes)
            // 76223, // Liurnia of the Lakes - Liurnia of the Lakes - Eastern Liurnia Lake Shore
            // 76222, // Liurnia of the Lakes - Liurnia of the Lakes - Gate Town Bridge
            // 76221, // Liurnia of the Lakes - Liurnia of the Lakes - Liurnia Highway North
            // 76244, // Liurnia of the Lakes - Liurnia of the Lakes - Liurnia Highway South
            // 76206, // Liurnia of the Lakes - Liurnia of the Lakes - Main Academy Gate
            // 76203, // Liurnia of the Lakes - Liurnia of the Lakes - Scenic Isle
            // 76205, // Liurnia of the Lakes - Liurnia of the Lakes - South Raya Lucaria Gate
            // 76225, // Liurnia of the Lakes - Liurnia of the Lakes - Ruined Labyrinth
            // 76216, // Liurnia of the Lakes - Liurnia of the Lakes - Boilprawn Shack
            // 76224, // Liurnia of the Lakes - Liurnia of the Lakes - Church of Vows
            // 76237, // Liurnia of the Lakes - Liurnia of the Lakes - Converted Tower
            // 76234, // Liurnia of the Lakes - Liurnia of the Lakes - Eastern Tableland
            // 76236, // Liurnia of the Lakes - Liurnia of the Lakes - Fallen Ruins of the Lake
            // 76219, // Liurnia of the Lakes - Liurnia of the Lakes - Folly on the Lake
            // 76245, // Liurnia of the Lakes - Liurnia of the Lakes - Jarburg
            // 76226, // Liurnia of the Lakes - Liurnia of the Lakes - Mausoleum Compound
            // 76247, // Liurnia of the Lakes - Liurnia of the Lakes - Ranni's Chamber
            // 76218, // Liurnia of the Lakes - Liurnia of the Lakes - Revenger's Shack
            // 76215, // Liurnia of the Lakes - Liurnia of the Lakes - Slumbering Wolf's Shack
            // 76220, // Liurnia of the Lakes - Liurnia of the Lakes - Village of the Albinaurics
            // 76243, // Liurnia of the Lakes - Liurnia of the Lakes - Crystalline Woods
            // 76242, // Liurnia of the Lakes - Liurnia of the Lakes - East Gate Bridge Trestle
            // 76210, // Liurnia of the Lakes - Liurnia of the Lakes - Foot of the Four Belfries
            // 76233, // Liurnia of the Lakes - Liurnia of the Lakes - Gate Town North
            // 76214, // Liurnia of the Lakes - Liurnia of the Lakes - Main Caria Manor Gate
            // 76231, // Liurnia of the Lakes - Liurnia of the Lakes - Manor Lower Level
            // 76230, // Liurnia of the Lakes - Liurnia of the Lakes - Manor Upper Level
            // 76212, // Liurnia of the Lakes - Liurnia of the Lakes - Northern Liurnia Lake Shore
            // 76213, // Liurnia of the Lakes - Liurnia of the Lakes - Road to the Manor
            // 76211, // Liurnia of the Lakes - Liurnia of the Lakes - Sorcerer's Isle
            // 76241, // Liurnia of the Lakes - Liurnia of the Lakes - Temple Quarter
            // 76227, // Liurnia of the Lakes - Liurnia of the Lakes - The Four Belfries
            // 73106, // Liurnia of the Lakes - Liurnia of the Lakes - Academy Crystal Cave
            // 76238, // Liurnia of the Lakes - Liurnia of the Lakes - Behind Caria Manor
            // 73005, // Liurnia of the Lakes - Liurnia of the Lakes - Black Knife Catacombs
            // 73006, // Liurnia of the Lakes - Liurnia of the Lakes - Cliffbottom Catacombs
            // 73105, // Liurnia of the Lakes - Liurnia of the Lakes - Lakeside Crystal Cave
            // 73421, // Liurnia of the Lakes - Liurnia of the Lakes - Liurnia Tower Bridge
            // 76228, // Liurnia of the Lakes - Liurnia of the Lakes - Ranni's Rise
            // 76229, // Liurnia of the Lakes - Liurnia of the Lakes - Ravine-Veiled Village
            // 73202, // Liurnia of the Lakes - Liurnia of the Lakes - Raya Lucaria Crystal Tunnel
            // 73003, // Liurnia of the Lakes - Liurnia of the Lakes - Road's End Catacombs
            // 73104, // Liurnia of the Lakes - Liurnia of the Lakes - Stillwater Cave
            // 73420, // Liurnia of the Lakes - Liurnia of the Lakes - Study Hall Entrance
            // 76235, // Liurnia of the Lakes - Liurnia of the Lakes - The Ravine
            // 73422, // Liurnia of the Lakes - Liurnia of the Lakes - Divine Tower of Liurnia
            // 76208, // Liurnia of the Lakes - Bellum Highway - Bellum Church
            // 76240, // Liurnia of the Lakes - Bellum Highway - Church of Inhibition
            // 76207, // Liurnia of the Lakes - Bellum Highway - East Raya Lucaria Gate
            // 76239, // Liurnia of the Lakes - Bellum Highway - Frenzied Flame Village Outskirts
            // 76209, // Liurnia of the Lakes - Bellum Highway - Grand Lift of Dectus
            // 73900, // Liurnia of the Lakes - Ruin-Strewn Precipice - Magma Wyrm
            // 73901, // Liurnia of the Lakes - Ruin-Strewn Precipice - Ruin-Strewn Precipice
            // 73902, // Liurnia of the Lakes - Ruin-Strewn Precipice - Ruin-Strewn Precipice Overlook
            // 76252, // Liurnia of the Lakes - Moonlight Altar - Altar South
            // 76251, // Liurnia of the Lakes - Moonlight Altar - Cathedral of Manus Celes
            // 76250, // Liurnia of the Lakes - Moonlight Altar - Moonlight Altar
            // 71402, // Academy of Raya Lucaria - Church of the Cuckoo
            // 71403, // Academy of Raya Lucaria - Schoolhouse Classroom
            // 76300, // Altus Plateau - Altus Plateau - Abandoned Coffin
            // 76303, // Altus Plateau - Altus Plateau - Altus Highway Junction
            // 76301, // Altus Plateau - Altus Plateau - Altus Plateau
            // 76306, // Altus Plateau - Altus Plateau - Bower of Bounty
            // 76302, // Altus Plateau - Altus Plateau - Erdtree-Gazing Hill
            // 76304, // Altus Plateau - Altus Plateau - Forest-Spanning Greatbridge
            // 76305, // Altus Plateau - Altus Plateau - Rampartside Path
            // 76307, // Altus Plateau - Altus Plateau - Road of Iniquity Side Path
            // 76321, // Altus Plateau - Altus Plateau - Shaded Castle Inner Gate
            // 76320, // Altus Plateau - Altus Plateau - Shaded Castle Ramparts
            // 76308, // Altus Plateau - Altus Plateau - Windmill Village
            // 73205, // Altus Plateau - Altus Plateau - Altus Tunnel
            // 73204, // Altus Plateau - Altus Plateau - Old Altus Tunnel
            // 73118, // Altus Plateau - Altus Plateau - Perfumer's Grotto
            // 73119, // Altus Plateau - Altus Plateau - Sage's Cave
            // 73008, // Altus Plateau - Altus Plateau - Sainted Hero's Grave
            // 73012, // Altus Plateau - Altus Plateau - Unsightly Catacombs
            // 76350, // Altus Plateau - Mt. Gelmir - Bridge of Iniquity
            // 76356, // Altus Plateau - Mt. Gelmir - Craftsman's Shack
            // 76351, // Altus Plateau - Mt. Gelmir - First Mt. Gelmir Campsite
            // 73009, // Altus Plateau - Mt. Gelmir - Gelmir Hero's Grave
            // 76352, // Altus Plateau - Mt. Gelmir - Ninth Mt. Gelmir Campsite
            // 76357, // Altus Plateau - Mt. Gelmir - Primeval Sorcerer Azur
            // 76353, // Altus Plateau - Mt. Gelmir - Road of Iniquity
            // 73107, // Altus Plateau - Mt. Gelmir - Seethewater Cave
            // 76354, // Altus Plateau - Mt. Gelmir - Seethewater River
            // 76355, // Altus Plateau - Mt. Gelmir - Seethewater Terminus
            // 73109, // Altus Plateau - Mt. Gelmir - Volcano Cave
            // 73007, // Altus Plateau - Mt. Gelmir - Wyndham Catacombs
            // 73013, // Altus Plateau - Leyndell, Royal Capital - Auriza Side Tomb
            // 73010, // Altus Plateau - Leyndell, Royal Capital - Auzira Hero's Grave
            // 76314, // Altus Plateau - Leyndell, Royal Capital - Capital Rampart
            // 73430, // Altus Plateau - Leyndell, Royal Capital - Divine Tower of West Altus
            // 73432, // Altus Plateau - Leyndell, Royal Capital - Divine Tower of West Altus, --  Gate
            // 76311, // Altus Plateau - Leyndell, Royal Capital - Hermit Merchant's Shack
            // 76310, // Altus Plateau - Leyndell, Royal Capital - Minor Erdtree Church
            // 76312, // Altus Plateau - Leyndell, Royal Capital - Outer Wall Battleground
            // 76309, // Altus Plateau - Leyndell, Royal Capital - Outer Wall Phantom Tree
            // 73431, // Altus Plateau - Leyndell, Royal Capital - Sealed Tunnel
            // 71605, // Volcano Manor - Audience Pathway
            // 71604, // Volcano Manor - Guest Hall
            // 71603, // Volcano Manor - Prison Town Church
            // 71607, // Volcano Manor - Subterranean Inquisition Chamber
            // 71602, // Volcano Manor - Volcano Manor
            // 71103, // Leyndell, Royal Capital - Leyndell, Royal Capital - Lower Capital Church
            // 71102, // Leyndell, Royal Capital - Leyndell, Royal Capital - East Capital Rampart
            // 71109, // Leyndell, Royal Capital - Leyndell, Royal Capital - Divine Bridge
            // 71108, // Leyndell, Royal Capital - Leyndell, Royal Capital - Fortified Manor, First Floor
            // 71107, // Leyndell, Royal Capital - Leyndell, Royal Capital - Queen's Bedchamber
            // 71105, // Leyndell, Royal Capital - Leyndell, Royal Capital - West Capital Rampart
            // 71104, // Leyndell, Royal Capital - Leyndell, Royal Capital - Avenue Balcony
            // 73502, // Leyndell, Royal Capital - Subterranean Shunning-Grounds - Forsaken Depths
            // 73504, // Leyndell, Royal Capital - Subterranean Shunning-Grounds - Frenzied Flame Proscription
            // 73503, // Leyndell, Royal Capital - Subterranean Shunning-Grounds - Leyndell Catacombs
            // 73501, // Leyndell, Royal Capital - Subterranean Shunning-Grounds - Underground Roadside
            // 76403, // Caelid - Caelid - Caelem Ruins
            // 76405, // Caelid - Caelid - Caelid Highway South
            // 76404, // Caelid - Caelid - Cathedral of Dragon Communion
            // 76415, // Caelid - Caelid - Chair-Crypt of Sellia
            // 76418, // Caelid - Caelid - Church of the Plague
            // 76402, // Caelid - Caelid - Fort Gael North
            // 76401, // Caelid - Caelid - Rotview Balcony
            // 76414, // Caelid - Caelid - Sellia Backstreets
            // 76416, // Caelid - Caelid - Sellia Under-Stair
            // 76400, // Caelid - Caelid - Smoldering Church
            // 76409, // Caelid - Caelid - Smoldering Wall
            // 76411, // Caelid - Caelid - Southern Aeonia Swamp Bank
            // 73120, // Caelid - Caelid - Abandoned Cave
            // 73015, // Caelid - Caelid - Caelid Catacombs
            // 76420, // Caelid - Caelid - Chamber Outside the Plaza
            // 76410, // Caelid - Caelid - Deep Siofra Well
            // 73207, // Caelid - Caelid - Gael Tunnel
            // 73121, // Caelid - Caelid - Gaol Cave
            // 76417, // Caelid - Caelid - Impassable Greatbridge
            // 73014, // Caelid - Caelid - Minor Erdtree Catacombs
            // 73257, // Caelid - Caelid - Rear Gael Tunnel Entrance
            // 73208, // Caelid - Caelid - Sellia Crystal Tunnel
            // 73016, // Caelid - Caelid - War-Dead Catacombs
            // 76406, // Caelid - Swamp of Aeonia - Aeonia Swamp Shore
            // 76407, // Caelid - Swamp of Aeonia - Astray from Caelid Highway North
            // 76413, // Caelid - Swamp of Aeonia - Inner Aeonia
            // 76454, // Caelid - Greyoll's Dragonbarrow - Bestial Sanctum
            // 73440, // Caelid - Greyoll's Dragonbarrow - Divine Tower of Caelid, --  Basement
            // 73441, // Caelid - Greyoll's Dragonbarrow - Divine Tower of Caelid, --  Center
            // 73110, // Caelid - Greyoll's Dragonbarrow - Dragonbarrow Cave
            // 76452, // Caelid - Greyoll's Dragonbarrow - Dragonbarrow Fork
            // 76450, // Caelid - Greyoll's Dragonbarrow - Dragonbarrow West
            // 76456, // Caelid - Greyoll's Dragonbarrow - Farum Greatbridge
            // 76453, // Caelid - Greyoll's Dragonbarrow - Fort Faroth
            // 73460, // Caelid - Greyoll's Dragonbarrow - Isolated Divine Tower
            // 76451, // Caelid - Greyoll's Dragonbarrow - Isolated Merchant's Shack (Greyoll's Dragonbarrow)
            // 76455, // Caelid - Greyoll's Dragonbarrow - Lenne's Rise
            // 73111, // Caelid - Greyoll's Dragonbarrow - Sellia Hideaway
            // 73451, // Mountaintops of the Giants - Forbiden Lands - Divine Tower of the East Altus
            // 73450, // Mountaintops of the Giants - Forbiden Lands - Divine Tower of the East Altus, --  Gate
            // 76500, // Mountaintops of the Giants - Forbiden Lands - Forbidden Lands
            // 76502, // Mountaintops of the Giants - Forbiden Lands - Grand Lift of Rold
            // 73020, // Mountaintops of the Giants - Forbiden Lands - Hidden Path to the Haligtree
            // 76503, // Mountaintops of the Giants - Mountaintops of the Giants - Ancient Snow Valley Ruins
            // 76522, // Mountaintops of the Giants - Mountaintops of the Giants - Castle Sol Main Gate
            // 76523, // Mountaintops of the Giants - Mountaintops of the Giants - Church of the Eclipse
            // 76505, // Mountaintops of the Giants - Mountaintops of the Giants - First Church of Marika
            // 76504, // Mountaintops of the Giants - Mountaintops of the Giants - Freezing Lake
            // 76521, // Mountaintops of the Giants - Mountaintops of the Giants - Snow Valley Ruins Overlook
            // 73122, // Mountaintops of the Giants - Mountaintops of the Giants - Spiritcaller's Cave
            // 76520, // Mountaintops of the Giants - Mountaintops of the Giants - Whiteridge Road
            // 76501, // Mountaintops of the Giants - Mountaintops of the Giants - Zamor Ruins
            // 76507, // Mountaintops of the Giants - Flame Peak - Church of Repose
            // 76508, // Mountaintops of the Giants - Flame Peak - Foot of the Forge
            // 76510, // Mountaintops of the Giants - Flame Peak - Forge of the Giants
            // 76506, // Mountaintops of the Giants - Flame Peak - Giant's Gravepost
            // 73018, // Mountaintops of the Giants - Flame Peak - Giant's Mountaintop Catacombs
            // 73017, // Mountaintops of the Giants - Flame Peak - Giant-Conquering Hero's Grave
            // 76653, // Mountaintops of the Giants - Consecrated Snowfield - Apostate Derelict
            // 73112, // Mountaintops of the Giants - Consecrated Snowfield - Cave of the Forlorn
            // 76550, // Mountaintops of the Giants - Consecrated Snowfield - Consecrated Snowfield
            // 73019, // Mountaintops of the Giants - Consecrated Snowfield - Consecrated Snowfield Catacombs
            // 76551, // Mountaintops of the Giants - Consecrated Snowfield - Inner Consecrated Snowfield
            // 76652, // Mountaintops of the Giants - Consecrated Snowfield - Ordina, Liturgical Town
            // 73211, // Mountaintops of the Giants - Consecrated Snowfield - Yelough Anix Tunnel
            // 71506, // Miquella's Haligtree - Miquella's Haligtree - Haligtree Canopy
            // 71507, // Miquella's Haligtree - Miquella's Haligtree - Haligtree Town
            // 71508, // Miquella's Haligtree - Miquella's Haligtree - Haligtree Town Plaza
            // 71503, // Miquella's Haligtree - Elphael, Brace of the Haligtree - Drainage Channel
            // 71502, // Miquella's Haligtree - Elphael, Brace of the Haligtree - Elphael Inner Wall
            // 71504, // Miquella's Haligtree - Elphael, Brace of the Haligtree - Haligtree Roots
            // 71501, // Miquella's Haligtree - Elphael, Brace of the Haligtree - Prayer Room
            // 71310, // Crumbling Farum Azula - Beside the great Bridge
            // 71303, // Crumbling Farum Azula - Crumbling Beast Grave
            // 71304, // Crumbling Farum Azula - Crumbling Beast Grave Depths
            // 71306, // Crumbling Farum Azula - Dragon Temple
            // 71308, // Crumbling Farum Azula - Dragon Temple Lift
            // 71309, // Crumbling Farum Azula - Dragon Temple Rooftop
            // 71307, // Crumbling Farum Azula - Dragon Temple Transept
            // 71305, // Crumbling Farum Azula - Tempest-Facing Balcony
            // 71213, // Ainsel River - Ainsel River - Ainsel River Downstream
            // 71212, // Ainsel River - Ainsel River - Ainsel River Sluice Gate
            // 71211, // Ainsel River - Ainsel River - Ainsel River Well Depths
            // 71214, // Ainsel River - Ainsel River Main - Ainsel River Main
            // 71215, // Ainsel River - Ainsel River Main - Nokstella, Eternal City
            // 71219, // Ainsel River - Ainsel River Main - Nokstella Waterfall Basin
            // 71218, // Ainsel River - Lake of Rot - Grand Cloister
            // 71216, // Ainsel River - Lake of Rot - Lake of Rot Shoreside
            // 71271, // Nokron, Eternal City - Nokron, Eternal City - Nokron, Eternal City
            // 71224, // Nokron, Eternal City - Nokron, Eternal City - Ancestral Woods
            // 71225, // Nokron, Eternal City - Nokron, Eternal City - Aqueduct-Facing Cliffs
            // 71226, // Nokron, Eternal City - Nokron, Eternal City - Night's Sacred Ground
            // 71227, // Nokron, Eternal City - Siofra River - Below the Well
            // 71222, // Nokron, Eternal City - Siofra River - Siofra River Bank
            // 71270, // Nokron, Eternal City - Siofra River - Siofra River Well Depths
            // 71223, // Nokron, Eternal City - Siofra River - Worshippers' Woods
            // 71252, // Nokron, Eternal City - Mohgwyn Palace - Dynasty Mausoleum Entrance
            // 71253, // Nokron, Eternal City - Mohgwyn Palace - Dynasty Mausoleum Midpoint
            // 71251, // Nokron, Eternal City - Mohgwyn Palace - Palace Approach Ledge-Road
            // 71235, // Deeproot Depths - Across the Roots
            // 71233, // Deeproot Depths - Deeproot Depths
            // 71232, // Deeproot Depths - Great Waterfall Crest
            // 71231, // Deeproot Depths - Root-Facing Cliffs
            // 71234, // Deeproot Depths - The Nameless Eternal City

            // Godric
            10000800,
            9101,
            
            // Rennala
            14000800,
            9118,
            71400, // Academy of Raya Lucaria - Raya Lucaria Grand Library
            
            // Radahn
            1252380800,
            9130,
            9413,
            76422, // Caelid - Caelid - Starscourge Radahn
            
            // Margott
            11000800,
            9104,
            
            // Rykard
            16000800,
            9122,

            // Fire Giant
            1252520800,
            9131,
            
            // Regal Ancestor Spirit
            12090800,
            9133,

            // Astel, Naturalborn of the Void
            12040800,
            9108,

            // Fortissax
            12030850,
            9111,

            // Mohg
            12050800,
            9112,
            71250, // Nokron, Eternal City - Mohgwyn Palace - Cocoon of the Empyrean
            
            // Maliketh
            13000800,
            9116,
            
            // Placidusax
            13000830,
            9115,
            
            // Malenia
            15000800,
            9120,
            
            // Godfrey
            11050800,
            9107,
                
            // Elden Beast
            19000800,
            19001100,
            9123,
        };
        List<int> mapFlags = new() {
            62010,  // Map: Limgrave, West
            62011,  // Map: Weeping Peninsula
            62012,  // Map: Limgrave, East
            62020,  // Map: Liurnia, East
            62021,  // Map: Liurnia, North
            62022,  // Map: Liurnia, West
            62030,  // Map: Altus Plateau
            62031,  // Map: Leyndell, Royal Capital
            62032,  // Map: Mt. Gelmir
            62040,  // Map: Caelid
            62041,  // Map: Dragonbarrow
            62050,  // Map: Mountaintops of the Giants, West
            62051,  // Map: Mountaintops of the Giants, East
            62060,  // Map: Ainsel River
            62061,  // Map: Lake of Rot
            62063,  // Map: Siofra River
            62062,  // Map: Mohgwyn Palace
            62064,  // Map: Deeproot Depths
            62052,  // Map: Consecrated Snowfield
            62080,  // Map: Gravesite Plain
            62081,  // Map: Scadu Altus
            62082,  // Map: Southern Shore
            62083,  // Map: Rauh Ruins
            62084,  // Map: Abyss
        };
        List<int> otherFlags = new() {
            82001, // Underground map layer enabled
            82002, // Show DLC Map
            10009655, // First approached about Roundtable
            105, // Visited Roundtable
        };
        EMEVD common = _resources.CommonEmevd;
        if (common == null) {
            throw new InvalidOperationException($"Missing emevd {Const.CommonEventPath}");
        }
        List<EMEVD.Instruction> newInstrs = new()
        {
            // EndIfEventFlag(End, ON, TargetEventFlagType.EventFlag, 60100)
            new EMEVD.Instruction(1003, 2, new List<object> { (byte)0, (byte)1, (byte)0, (uint)60100 }),
            // IfEventFlag(MAIN, ON, TargetEventFlagType.EventFlag, 60100)
            new EMEVD.Instruction(3, 0, new List<object> { (sbyte)0, (byte)1, (byte)0, (uint)60100 }),
            // OpenWorldMapPoint(111000, 100)
            // (May be redundant to unlocking all maps)
            new EMEVD.Instruction(2003, 78, new List<object> { 111000, 100f }),
        };
        foreach (int flag in bonfireFlags.Concat(mapFlags).Concat(otherFlags))
        {
            // SetEventFlag(TargetEventFlagType.EventFlag, flag, ON)
            newInstrs.Add(new EMEVD.Instruction(2003, 66, new List<object> { (byte)0, flag, (byte)1 }));
        }
        int newEventId = 279551111;  // Arbitrary number
        EMEVD.Event newEvent = new EMEVD.Event(newEventId, EMEVD.Event.RestBehaviorType.Default);
        newEvent.Instructions = newInstrs;
        common.Events.Add(newEvent);

        // Process to edit other events
        EMEVD.Event? constrEvent = common.Events.Find(e => e.ID == 0);
        EMEVD.Event? grantMapEvent = common.Events.Find(e => e.ID == 1600);
        EMEVD.Event? reachedAltusEvent = common.Events.Find(e => e.ID == 3044);
        if (constrEvent == null || grantMapEvent == null || reachedAltusEvent == null) {
            throw new InvalidOperationException($"{Const.CommonEventPath} missing one of required events [0, 1600, 3044]");
        }
        // Initialize new event
        constrEvent.Instructions.Add(new EMEVD.Instruction(2000, 0, new List<object> { 0, newEventId, 0 }));
        // Map unlock spam fix
        for (int i = 0; i < grantMapEvent.Instructions.Count; i++)
        {
            EMEVD.Instruction ins = grantMapEvent.Instructions[i];
            if (ins.Bank == 2003 && ins.ID == 66)
            {
                // Label18()
                grantMapEvent.Instructions[i] = new EMEVD.Instruction(1014, 18);
                grantMapEvent.Parameters.RemoveAll(p => p.InstructionIndex == i);
            }
        }
        // Altus check. Dynamically rewriting this is possible but annoying, so recreate it from scratch.
        List<EMEVD.Instruction> rewriteInstrs = new() {
            // EndIfPlayerIsInWorldType(EventEndType.End, WorldType.OtherWorld)
            new EMEVD.Instruction(1003, 14, new List<object> { (byte)0, (byte)1 }),
            // SetEventFlag(TargetEventFlagType.EventFlag, 3063, OFF)
            new EMEVD.Instruction(2003, 66, new List<object> { (byte)0, (uint)3063, (byte)0 }),
            // Cut condition: IfBatchEventFlags(MAIN, LogicalOperationType.NotAllOFF, TargetEventFlagType.EventFlag, 76300, 76399)
            // IfBatchEventFlags(OR_01, LogicalOperationType.NotAllOFF, TargetEventFlagType.EventFlag, 76300, altusId - 1)
            new EMEVD.Instruction(3, 1, new List<object> { (sbyte)-1, (byte)2, (byte)0, 76300, altusId - 1 }),
            // IfBatchEventFlags(OR_01, LogicalOperationType.NotAllOFF, TargetEventFlagType.EventFlag, altusId + 1, gelmirId - 1)
            new EMEVD.Instruction(3, 1, new List<object> { (sbyte)-1, (byte)2, (byte)0, altusId + 1, gelmirId - 1 }),
            // IfBatchEventFlags(OR_01, LogicalOperationType.NotAllOFF, TargetEventFlagType.EventFlag, gelmirId + 1, 76399)
            new EMEVD.Instruction(3, 1, new List<object> { (sbyte)-1, (byte)2, (byte)0, gelmirId + 1, 76399 }),
            // IfConditionGroup(MAIN, ON, OR_01)
            new EMEVD.Instruction(0, 0, new List<object> { (sbyte)0, (byte)1, (sbyte)-1 }),
            // SetEventFlag(TargetEventFlagType.EventFlag, 3063, ON)
            new EMEVD.Instruction(2003, 66, new List<object> { (byte)0, (uint)3063, (byte)1 }),
            // EndUnconditionally(EventEndType.End)
        };
        reachedAltusEvent.Instructions = rewriteInstrs;
        reachedAltusEvent.Parameters.Clear();
    }
    private void patchAtkParam() {
        Param.Row swarmOfFlies1 = _resources.AtkParamPc[72100] ??
                                  throw new InvalidOperationException("Entry 72100 not found in AtkParam_Pc");
        Param.Row swarmOfFlies2 = _resources.AtkParamPc[72101] ??
                                  throw new InvalidOperationException("Entry 72101 not found in AtkParam_Pc");

        AtkParam swarmAtkParam1 = new(swarmOfFlies1);
        AtkParam swarmAtkParam2 = new(swarmOfFlies2);
        patchSpEffectAtkPowerCorrectRate(swarmAtkParam1);
        patchSpEffectAtkPowerCorrectRate(swarmAtkParam2);
    }
    private static void patchSpEffectAtkPowerCorrectRate(AtkParam atkParam) {
        atkParam.spEffectAtkPowerCorrectRate_byPoint = 100;
        atkParam.spEffectAtkPowerCorrectRate_byRate = 100;
        atkParam.spEffectAtkPowerCorrectRate_byDmg = 100;
    }
}
