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

public struct RandomizeRule
{
    public string Seed;
    public bool RandomStartupClasses;
    public bool RandomWeapons;
    public bool OpenGraces;
    public bool ReduceUpgradeMat;
    public int ReduceUpgradeMatType;
    public KeyValuePair<int, int>[] StartupItems;
}

public partial class BingoRandomizer
{
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
        CancellationToken cancellationToken)
    {
        BingoRandomizer rando = new(path, rule, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Run(() => rando.init(), cancellationToken);
        return rando;
    }

    // Cancellation Token
    private readonly CancellationToken _cancellationToken;

    private BingoRandomizer(string path, RandomizeRule rule, CancellationToken cancellationToken)
    {
        _resources = new RandoResource(path, rule.Seed, cancellationToken);
        _randomStartupClasses = rule.RandomStartupClasses;
        _randomWeapons = rule.RandomWeapons;
        _openGraces = rule.OpenGraces;
        _reduceUpgradeMatType = rule.ReduceUpgradeMat ? rule.ReduceUpgradeMatType : -1;
        _startupItems = rule.StartupItems;
        _classRandomizer = new Season3ClassRandomizer(new Season2LevelRandomizer(_resources.Random), _resources);
        _cancellationToken = cancellationToken;
    }

    private Task init()
    {
        return _resources.Init();
    }

    public Task RandomizeRegulation()
    {
        //calculateLevels();
        if (_randomStartupClasses)
        {
            _classRandomizer.RandomizeCharaInitParam();
            _cancellationToken.ThrowIfCancellationRequested();
        }

        if (_randomWeapons)
        {
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
        if (_openGraces)
        {
            unlockSeason3Graces();
            _cancellationToken.ThrowIfCancellationRequested();
        }

        _cancellationToken.ThrowIfCancellationRequested();
        addStartupItems();
        _cancellationToken.ThrowIfCancellationRequested();
        if (_openGraces)
        {
            increaseItemLotChance();
            _cancellationToken.ThrowIfCancellationRequested();
        }

        writeFiles();
        Logger.WriteLog(_resources.Seed);
        return Task.CompletedTask;
    }

    private void changeUpgradeMaterialType(int type)
    {
        foreach (Param.Row row in _resources.EquipMtrlSetParam.Rows)
        {
            EquipMtrlSetParam mtrl = new EquipMtrlSetParam(row);

            int id = mtrl.materialId01;
            int cat = mtrl.materialCate01;
            int num = mtrl.itemNum01;
            if (cat == 4 && id >= 10100 && id < 10110 && num > 1)
            {
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

    public string GetSeed()
    {
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
        if (common == null)
        {
            throw new InvalidOperationException($"Missing emevd {Const.CommonEventPath}");
        }

        var newEventId = 279551112; // Arbitrary number
        List<EMEVD.Instruction> newInstrs =
        [
            new EMEVD.Instruction(1003, 2, new List<object> { (byte)0, (byte)1, (byte)0, (uint)60000 }),
            // IfEventFlag(MAIN, ON, TargetEventFlagType.EventFlag, 60000)
            new EMEVD.Instruction(3, 0, new List<object> { (sbyte)0, (byte)1, (byte)0, (uint)60000 })
        ];
        for (var i = 0; i < _startupItems.Length; i++)
        {
            var (id, cat) = _startupItems[i];
            addItemWithLotID(70010 + i, id, cat, 1);
            if (i % 10 == 0)
            {
                // AwardItemLot(70010 + i)
                newInstrs.Add(new EMEVD.Instruction(2003, 4, new List<object> { 70010 + i }));
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
        if (constrEvent == null)
        {
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

    private void randomizeItemLotParams()
    {
        OrderedDictionary categoryDictEnemy = new();
        OrderedDictionary categoryDictMap = new();

        IEnumerable<Param.Row> itemLotParamMap =
            _resources.ItemLotParamMap.Rows.Where(id => !Unk.unkItemLotParamMapWeapons.Contains(id.ID));
        IEnumerable<Param.Row> itemLotParamEnemy =
            _resources.ItemLotParamEnemy.Rows.Where(id => !Unk.unkItemLotParamEnemyWeapons.Contains(id.ID));

        foreach (Param.Row row in itemLotParamEnemy.Concat(itemLotParamMap))
        {
            Param.Column[] itemIds = row.Columns.Take(Const.ItemLots).ToArray();
            Param.Column[] categories = row.Columns.Skip(Const.CategoriesStart).Take(Const.ItemLots).ToArray();
            Param.Column[] chances = row.Columns.Skip(Const.ChanceStart).Take(Const.ItemLots).ToArray();
            int totalWeight = chances.Sum(a => (ushort)a.GetValue(row));
            for (int i = 0; i < Const.ItemLots; i++)
            {
                int category = (int)categories[i].GetValue(row);
                if (category != Const.ItemLotWeaponCategory && category != Const.ItemLotCustomWeaponCategory)
                {
                    continue;
                }

                int id = (int)itemIds[i].GetValue(row);
                int sanitizedId = RemoveWeaponLevels(id);
                if (category == Const.ItemLotWeaponCategory)
                {
                    if (!_resources.WeaponDictionary.TryGetValue(sanitizedId, out EquipParamWeapon? wep))
                    {
                        continue;
                    }

                    if (wep.wepType is WeaponType.GlintstoneStaff or WeaponType.FingerSeal)
                    {
                        continue;
                    }

                    if (id != sanitizedId)
                    {
                        int difference = id - sanitizedId;
                        string differenceString = difference != 0 ? $" +{difference}" : string.Empty;
                        _resources.WeaponNameDictionary[id] =
                            $"{_resources.WeaponNameDictionary[sanitizedId]}{differenceString}";
                    }

                    ushort chance = (ushort)chances[i].GetValue(row);
                    if (chance == totalWeight)
                    {
                        addToOrderedDict(categoryDictMap, wep.wepType, new ItemLotEntry(id, category));
                        break; // Break here because the entire item lot param is just a single entry.
                    }

                    addToOrderedDict(categoryDictEnemy, wep.wepType, new ItemLotEntry(id, category));
                }
                else
                {
                    // category == Const.ItemLotCustomWeaponCategory
                    if (!_resources.CustomWeaponDictionary.TryGetValue(id, out EquipParamWeapon? wep))
                    {
                        continue;
                    }

                    if (wep.wepType is WeaponType.GlintstoneStaff or WeaponType.FingerSeal)
                    {
                        continue;
                    }

                    Param.Row paramRow = _resources.EquipParamCustomWeapon[id]!;
                    EquipParamCustomWeapon customWeapon = new(paramRow);
                    if (!_resources.WeaponNameDictionary.ContainsKey(customWeapon.baseWepId))
                    {
                        int baseWeaponId = customWeapon.baseWepId;
                        int customSanitizedId = RemoveWeaponLevels(baseWeaponId);
                        int difference = baseWeaponId - customSanitizedId;
                        string differenceString = difference != 0 ? $" +{difference}" : string.Empty;
                        _resources.WeaponNameDictionary[id] =
                            $"{_resources.WeaponNameDictionary[baseWeaponId]}{differenceString}";
                    }

                    ushort chance = (ushort)chances[i].GetValue(row);
                    if (chance == totalWeight)
                    {
                        addToOrderedDict(categoryDictMap, wep.wepType, new ItemLotEntry(id, category));
                        break;
                    }

                    addToOrderedDict(categoryDictEnemy, wep.wepType, new ItemLotEntry(id, category));
                }
            }
        }

        foreach (Param.Row row in _resources.ShopLineupParam.Rows)
        {
            if ((byte)row["equipType"]!.Value.Value != Const.ShopLineupWeaponCategory || row.ID >= 101900)
            {
                continue;
            }

            ShopLineupParam lot = new(new Param.Row(row));
            int sanitizedId = RemoveWeaponLevels(lot.equipId);
            if (!_resources.WeaponDictionary.TryGetValue(sanitizedId, out EquipParamWeapon? wep))
            {
                continue;
            }

            if (wep.wepType is WeaponType.GlintstoneStaff or WeaponType.FingerSeal)
            {
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

        foreach (Param.Row row in _resources.ItemLotParamEnemy.Rows.Concat(_resources.ItemLotParamMap.Rows))
        {
            Param.Column[] itemIds = row.Columns.Take(Const.ItemLots).ToArray();
            Param.Column[] categories = row.Columns.Skip(Const.CategoriesStart).Take(Const.ItemLots).ToArray();
            for (int i = 0; i < Const.ItemLots; i++)
            {
                int category = (int)categories[i].GetValue(row);
                if (category != Const.ItemLotWeaponCategory && category != Const.ItemLotCustomWeaponCategory)
                {
                    continue;
                }

                int id = (int)itemIds[i].GetValue(row);
                if (category == Const.ItemLotWeaponCategory)
                {
                    if (!_resources.WeaponDictionary.TryGetValue(RemoveWeaponLevels(id), out _))
                    {
                        continue;
                    }

                    if (guaranteedDropReplace.TryGetValue(id, out ItemLotEntry entry))
                    {
                        itemIds[i].SetValue(row, entry.Id);
                        categories[i].SetValue(row, entry.Category);
                        break;
                    }

                    if (!chanceDropReplace.TryGetValue(id, out entry))
                    {
                        continue;
                    }

                    itemIds[i].SetValue(row, entry.Id);
                    categories[i].SetValue(row, entry.Category);
                }
                else
                {
                    // category == Const.ItemLotCustomWeaponCategory
                    if (!_resources.CustomWeaponDictionary.TryGetValue(id, out _))
                    {
                        continue;
                    }

                    if (guaranteedDropReplace.TryGetValue(id, out ItemLotEntry entry))
                    {
                        itemIds[i].SetValue(row, entry.Id);
                        categories[i].SetValue(row, entry.Category);
                    }

                    if (!chanceDropReplace.TryGetValue(id, out entry))
                    {
                        continue;
                    }

                    itemIds[i].SetValue(row, entry.Id);
                    categories[i].SetValue(row, entry.Category);
                }
            }
        }
    }

    private void dumpCategoriesAndNames(OrderedDictionary dictionary)
    {
        foreach (object? key in dictionary.Keys)
        {
            List<ItemLotEntry> list = (List<ItemLotEntry>)dictionary[key]!;
            EquipParamWeapon.WeaponType type = (EquipParamWeapon.WeaponType)key;
            Console.WriteLine($"{type}");
            foreach (ItemLotEntry itemLotEntry in list)
            {
                int id = RemoveWeaponLevels(itemLotEntry.Id);
                string name = _resources.WeaponFmg[0][id];
                if (string.IsNullOrWhiteSpace(name))
                {
                    name = $"{_resources.WeaponNameDictionary[itemLotEntry.Id]}";
                }

                Console.WriteLine($"\t{name}");
            }
        }
    }

    private void dumpCategoriesAndCounts(OrderedDictionary dictionary)
    {
        foreach (object? key in dictionary.Keys)
        {
            List<ItemLotEntry> list = (List<ItemLotEntry>)dictionary[key]!;
            EquipParamWeapon.WeaponType type = (EquipParamWeapon.WeaponType)key;
            Console.WriteLine($"{type} = {list.Count}");
        }
    }

    private void randomizeShopLineupParam()
    {
        List<ShopLineupParam> shopLineupParamRemembranceList = new();
        foreach (Param.Row row in _resources.ShopLineupParam.Rows)
        {
            if ((byte)row["equipType"]!.Value.Value != Const.ShopLineupWeaponCategory ||
                (row.ID < 101900 || row.ID > 101980))
            {
                continue;
            }

            ShopLineupParam lot = new(new Param.Row(row));
            int sanitizedId = RemoveWeaponLevels(lot.equipId);
            if (!_resources.WeaponDictionary.TryGetValue(sanitizedId, out _))
            {
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

        foreach (Param.Row row in _resources.ShopLineupParam.Rows)
        {
            logShopId(row.ID);
            if ((byte)row["equipType"]!.Value.Value != Const.ShopLineupWeaponCategory || row.ID > 101980)
            {
                continue;
            }

            ShopLineupParam lot = new(row);
            if (!_resources.WeaponDictionary.TryGetValue(RemoveWeaponLevels(lot.equipId), out EquipParamWeapon? wep))
            {
                continue;
            }

            if (wep.wepType is WeaponType.GlintstoneStaff or WeaponType.FingerSeal)
            {
                continue;
            }

            replaceShopLineupParam(lot, shopLineupParamList, shopLineupParamRemembranceList);
        }
    }

    private void randomizeShopLineupParamMagic()
    {
        OrderedDictionary magicCategoryDictMap = new();
        List<ShopLineupParam> shopLineupParamRemembranceList = new();
        List<ShopLineupParam> shopLineupParamDragonList = new();
        foreach (Param.Row row in _resources.ShopLineupParam.Rows)
        {
            if ((byte)row["equipType"]!.Value.Value != Const.ShopLineupGoodsCategory || row.ID > 101980)
            {
                continue;
            }

            ShopLineupParam lot = new(new Param.Row(row));
            if (!_resources.MagicDictionary.TryGetValue(lot.equipId, out Magic? magic))
            {
                continue;
            }

            if (row.ID < 101950)
            {
                if (lot.mtrlId == -1)
                {
                    addToOrderedDict(magicCategoryDictMap, magic.ezStateBehaviorType, lot.equipId);
                    continue;
                }

                shopLineupParamRemembranceList.Add(lot);
            }
            else
            {
                // Dragon Communion Shop 101950 - 101980 
                shopLineupParamDragonList.Add(lot);
            }
        }

        foreach (Param.Row row in _resources.ItemLotParamEnemy.Rows.Concat(_resources.ItemLotParamMap.Rows))
        {
            Param.Column[] itemIds = row.Columns.Take(Const.ItemLots).ToArray();
            Param.Column[] categories = row.Columns.Skip(Const.CategoriesStart).Take(Const.ItemLots).ToArray();
            Param.Column[] chances = row.Columns.Skip(Const.ChanceStart).Take(Const.ItemLots).ToArray();
            int totalWeight = chances.Sum(a => (ushort)a.GetValue(row));
            for (int i = 0; i < Const.ItemLots; i++)
            {
                int category = (int)categories[i].GetValue(row);
                if (category != Const.ItemLotGoodsCategory)
                {
                    continue;
                }

                int id = (int)itemIds[i].GetValue(row);
                if (!_resources.MagicDictionary.TryGetValue(id, out Magic? magic))
                {
                    continue;
                }

                ushort chance = (ushort)chances[i].GetValue(row);
                if (chance == totalWeight)
                {
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
        foreach (Param.Row row in _resources.ShopLineupParam.Rows)
        {
            logShopIdMagic(row.ID);
            if ((byte)row["equipType"]!.Value.Value != Const.ShopLineupGoodsCategory || row.ID > 101980)
            {
                continue;
            }

            ShopLineupParam lot = new(row);
            if (!_resources.MagicDictionary.TryGetValue(lot.equipId, out _))
            {
                continue;
            }

            if (row.ID < 101950)
            {
                replaceShopLineupParamMagic(lot, magicShopReplacement, shopLineupParamRemembranceList);
            }
            else
            {
                ShopLineupParam newDragonIncant = getNewId(lot.equipId, shopLineupParamDragonList);
                Logger.LogItem($"{_resources.GoodsFmg[0][lot.equipId]} -> {_resources.GoodsFmg[0][newDragonIncant.equipId]}");
                copyShopLineupParam(lot, newDragonIncant);
            }
        }

        foreach (Param.Row row in _resources.ItemLotParamEnemy.Rows.Concat(_resources.ItemLotParamMap.Rows))
        {
            Param.Column[] itemIds = row.Columns.Take(Const.ItemLots).ToArray();
            Param.Column[] categories = row.Columns.Skip(Const.CategoriesStart).Take(Const.ItemLots).ToArray();
            for (int i = 0; i < Const.ItemLots; i++)
            {
                int category = (int)categories[i].GetValue(row);
                if (category != Const.ItemLotGoodsCategory)
                {
                    continue;
                }

                int id = (int)itemIds[i].GetValue(row);
                if (!_resources.MagicDictionary.TryGetValue(id, out Magic _))
                {
                    continue;
                }

                if (!magicShopReplacement.TryGetValue(id, out int entry))
                {
                    continue;
                }

                itemIds[i].SetValue(row, entry);
            }
        }
    }

    private void unlockSeason3Graces()
    {
        // There are a few parts to this:
        // - Set many flags when Torrent is unlocked. Do this every time we load in, especially if the mod gets updated or wasn't installed right.
        // - To avoid map notification spam, don't set the event flag which notifies the in-game map to do the notification.
        // - Adjust logic for Altus detection event so the custom bonfires don't count for it.

        int altusId = 76303; // Altus Highway Junction
        int gelmirId = 76353; // Road of Iniquity
        List<int> bonfireFlags = new()
        {
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
            9414,
            9415,
            9416,
            9417,
            310,

            // Margott
            11000800,
            9104,
            4600,
            400001,

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
            118,

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
        List<int> mapFlags = new()
        {
            62010, // Map: Limgrave, West
            62011, // Map: Weeping Peninsula
            62012, // Map: Limgrave, East
            62020, // Map: Liurnia, East
            62021, // Map: Liurnia, North
            62022, // Map: Liurnia, West
            62030, // Map: Altus Plateau
            62031, // Map: Leyndell, Royal Capital
            62032, // Map: Mt. Gelmir
            62040, // Map: Caelid
            62041, // Map: Dragonbarrow
            62050, // Map: Mountaintops of the Giants, West
            62051, // Map: Mountaintops of the Giants, East
            62060, // Map: Ainsel River
            62061, // Map: Lake of Rot
            62063, // Map: Siofra River
            62062, // Map: Mohgwyn Palace
            62064, // Map: Deeproot Depths
            62052, // Map: Consecrated Snowfield
            62080, // Map: Gravesite Plain
            62081, // Map: Scadu Altus
            62082, // Map: Southern Shore
            62083, // Map: Rauh Ruins
            62084, // Map: Abyss
        };
        List<int> otherFlags = new()
        {
            82001, // Underground map layer enabled
            82002, // Show DLC Map
            10009655, // First approached about Roundtable
            105, // Visited Roundtable

            // Tailors
            60140,
            60150,

            65600, // Upgrade - Standard
            60130, // Ashes of War
            65610, // Iron Whetblade (Heavy)
            65620, // Iron Whetblade (Keen)
            65630, // Iron Whetblade (Quality)
            65640, // Red-Hot Whetblade (Fire)
            65650, // Red-Hot Whetblade (Flame Art)
            65660, // Sanctified Whetblade (Lightning)
            65670, // Sanctified Whetblade (Sacred)
            65680, // Glintstone Whetblade (Magic)
            65690, // Glintstone Whetblade (Frost)
            65700, // Black Whetblade (Poison)
            65710, // Black Whetblade (Blood)
            65720, // Black Whetblade (Occult)

            60120, // Unlock Crafting
            67610, // Missionary's Cookbook [1]
            67600, // Missionary's Cookbook [2]
            67650, // Missionary's Cookbook [3]
            67640, // Missionary's Cookbook [4]
            67630, // Missionary's Cookbook [5]
            67130, // Missionary's Cookbook [6]
            68230, // Missionary's Cookbook [7]
            67000, // Nomadic warrior's Cookbook [1]
            67110, // Nomadic warrior's Cookbook [2]
            67010, // Nomadic Warrior's Cookbook [3]
            67800, // Nomadic Warrior's Cookbook [4]
            67830, // Nomadic Warrior's Cookbook [5]
            67020, // Nomadic Warrior's Cookbook [6]
            67050, // Nomadic Warrior's Cookbook [7]
            67880, // Nomadic Warrior's Cookbook [8]
            67430, // Nomadic Warrior's Cookbook [9]
            67030, // Nomadic Warrior's Cookbook [10]
            67220, // Nomadic Warrior's Cookbook [11]
            67060, // Nomadic Warrior's Cookbook [12]
            67080, // Nomadic Warrior's Cookbook [13]
            67870, // Nomadic Warrior's Cookbook [14]
            67900, // Nomadic Warrior's Cookbook [15]
            67290, // Nomadic Warrior's Cookbook [16]
            67100, // Nomadic Warrior's Cookbook [17]
            67270, // Nomadic Warrior's Cookbook [18]
            67070, // Nomadic Warrior's Cookbook [19]
            67230, // Nomadic Warrior's Cookbook [20]
            67120, // Nomadic Warrior's Cookbook [21]
            67890, // Nomadic Warrior's Cookbook [22]
            67090, // Nomadic Warrior's Cookbook [23]
            67910, // Nomadic Warrior's Cookbook [24]
            67200, // Armorer's Cookbook [1]
            67210, // Armorer's Cookbook [2]
            67280, // Armorer's Cookbook [3]
            67260, // Armorer's Cookbook [4]
            67310, // Armorer's Cookbook [5]
            67300, // Armorer's Cookbook [6]
            67250, // Armorer's Cookbook [7]
            68000, // Ancient Dragon Apostle's Cookbook [1]
            68010, // Ancient Dragon Apostle's Cookbook [2]
            68030, // Ancient Dragon Apostle's Cookbook [3]
            68020, // Ancient Dragon Apostle's Cookbook [4]
            68200, // Fevor's Cookbook [1]
            68220, // Fevor's Cookbook [2]
            68210, // Fevor's Cookbook [3]
            67840, // Perfumer's Cookbook [1]
            67850, // Perfumer's Cookbook [2]
            67860, // Perfumer's Cookbook [3]
            67920, // Perfumer's Cookbook [4]
            67410, // Glintstone Craftsman's Cookbook [1]
            67450, // Glintstone Craftsman's Cookbook [2]
            67480, // Glintstone Craftsman's Cookbook [3]
            67400, // Glintstone Craftsman's Cookbook [4]
            67420, // Glintstone Craftsman's Cookbook [5]
            67460, // Glintstone Craftsman's Cookbook [6]
            67470, // Glintstone Craftsman's Cookbook [7]
            67440, // Glintstone Craftsman's Cookbook [8]
            68400, // Frenzied's Cookbook [1]
            68410, // Frenzied's Cookbook [2]
        };
        var gestures = new[]
        {
            0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 20, 21, 22, 23, 24, 25, 30, 40, 41, 50, 51, 52, 53, 54, 60, 70, 71, 72, 73, 80, 90, 91, 92, 93, 94, 95, 97, 98, 100, 101, 102, 103, 104, 105, 106, 108,
            109
        };
        var itemsToGive = new[]
        {
            // Great runes
            (191, 1, 1),
            (192, 1, 1),
            (193, 1, 1),
            (194, 1, 1),
            (195, 1, 1),
            (196, 1, 1),

            // Whetblades
            (8163, 1, 1),
            (8188, 1, 1),
            (8590, 1, 1),
            (8970, 1, 1),
            (8971, 1, 1),
            (8972, 1, 1),
            (8973, 1, 1),
            (8974, 1, 1),
            
            (190, 1, 50),
            (250, 1, 1),
            (2130, 1, 10),
            (2917, 1, 90),
            (2919, 1, 90),
            (8185, 1, 24),
            (9500, 1, 20),
            (9501, 1, 10),
            (9510, 1, 10),
            (10010, 1, 30),
            (10020, 1, 12),
            (10030, 1, 7),
            (10040, 1, 3),
            (10070, 1, 10),
            
            // Consumables
            (810, 1, 100),
            (811, 1, 100),
            (812, 1, 100),
            (820, 1, 100),
            (830, 1, 100),
            (900, 1, 100),
            (910, 1, 100),
            (920, 1, 100),
            (930, 1, 100),
            (940, 1, 100),
            (950, 1, 100),
            (960, 1, 100),
            (1100, 1, 100),
            (1110, 1, 100),
            (1120, 1, 100),
            (1130, 1, 100),
            (1140, 1, 100),
            (1150, 1, 100),
            (1160, 1, 100),
            (1170, 1, 100),
            (1180, 1, 100),
            (1190, 1, 100),
            (1200, 1, 100),
            (1210, 1, 100),
            (1235, 1, 100),
            (1290, 1, 100),
            (1310, 1, 100),
            (1320, 1, 100),
            (1330, 1, 100),
            (1340, 1, 100),
            (1400, 1, 100),
            (1410, 1, 100),
            (1420, 1, 100),
            (1430, 1, 100),
            (1440, 1, 100),
            (1450, 1, 100),
            (1460, 1, 100),
            (1470, 1, 100),
            (1480, 1, 100),
            (1490, 1, 100),
            (1500, 1, 100),
            (1510, 1, 100),
            (1520, 1, 100),
            (1530, 1, 100),
            (1540, 1, 100),
            (1550, 1, 100),
            (1560, 1, 100),
            (1590, 1, 100),
            (1690, 1, 100),
            (1700, 1, 100),
            (1710, 1, 100),
            (1720, 1, 100),
            (1730, 1, 100),
            (1740, 1, 100),
            (1750, 1, 100),
            (1760, 1, 100),
            (1830, 1, 100),
            (1831, 1, 100),
            (1840, 1, 100),
            (1841, 1, 100),
            (2020, 1, 100),
            (2030, 1, 100),
            (2050, 1, 100),
            (2100, 1, 100),
            (2120, 1, 100),
            (3030, 1, 100),
            (3050, 1, 100),
            (3051, 1, 100),
            (3060, 1, 100),
            (3070, 1, 100),
            (3310, 1, 100),
            (3311, 1, 100),
            (3320, 1, 100),
            (3350, 1, 100),
            (10000, 1, 100),
            
            // Materials
            (15000, 1, 200),
            (15010, 1, 200),
            (15020, 1, 200),
            (15030, 1, 200),
            (15040, 1, 200),
            (15050, 1, 200),
            (15060, 1, 200),
            (15080, 1, 200),
            (15090, 1, 200),
            (15100, 1, 200),
            (15110, 1, 200),
            (15120, 1, 200),
            (15130, 1, 200),
            (15140, 1, 200),
            (15150, 1, 200),
            (15160, 1, 200),
            (15340, 1, 200),
            (15341, 1, 200),
            (15400, 1, 200),
            (15410, 1, 200),
            (15420, 1, 200),
            (15430, 1, 200),
            (20650, 1, 200),
            (20651, 1, 200),
            (20652, 1, 200),
            (20653, 1, 200),
            (20654, 1, 200),
            (20660, 1, 200),
            (20680, 1, 200),
            (20681, 1, 200),
            (20682, 1, 200),
            (20683, 1, 200),
            (20685, 1, 200),
            (20690, 1, 200),
            (20691, 1, 200),
            (20710, 1, 200),
            (20720, 1, 200),
            (20721, 1, 200),
            (20722, 1, 200),
            (20723, 1, 200),
            (20740, 1, 200),
            (20750, 1, 200),
            (20751, 1, 200),
            (20753, 1, 200),
            (20760, 1, 200),
            (20761, 1, 200),
            (20770, 1, 200),
            (20775, 1, 200),
            (20780, 1, 200),
            (20795, 1, 200),
            (20800, 1, 200),
            (20801, 1, 200),
            (20802, 1, 200),
            (20810, 1, 200),
            (20811, 1, 200),
            (20812, 1, 200),
            (20820, 1, 200),
            (20825, 1, 200),
            (20830, 1, 200),
            (20831, 1, 200),
            (20840, 1, 200),
            (20841, 1, 200),
            (20842, 1, 200),
            (20845, 1, 200),
            (20850, 1, 200),
            (20852, 1, 200),
            (20855, 1, 200),
            
            // Tears
            (11000, 1, 1),
            (11001, 1, 1),
            (11002, 1, 1),
            (11003, 1, 1),
            (11004, 1, 1),
            (11005, 1, 1),
            (11006, 1, 1),
            (11007, 1, 1),
            (11008, 1, 1),
            (11009, 1, 1),
            (11010, 1, 1),
            (11011, 1, 1),
            (11012, 1, 1),
            (11013, 1, 1),
            (11014, 1, 1),
            (11015, 1, 1),
            (11016, 1, 1),
            (11017, 1, 1),
            (11018, 1, 1),
            (11019, 1, 1),
            (11020, 1, 1),
            (11021, 1, 1),
            (11022, 1, 1),
            (11023, 1, 1),
            (11024, 1, 1),
            (11025, 1, 1),
            (11026, 1, 1),
            (11027, 1, 1),
            (11028, 1, 1),
            (11029, 1, 1),
            (11030, 1, 1),
            (11031, 1, 1),

            // Bellrings
            (8951, 1, 1),
            (8952, 1, 1),
            (8953, 1, 1),
            (8954, 1, 1),
            (8955, 1, 1),
            (8956, 1, 1),
            (8957, 1, 1),
            (8958, 1, 1),
            (8959, 1, 1),
            (8960, 1, 1),
            (8961, 1, 1),
            (8962, 1, 1),
            (8963, 1, 1),
            (8964, 1, 1),
            (8965, 1, 1),

            // Spells
            (4000, 1, 1),
            (4010, 1, 1),
            (4001, 1, 1),
            (4020, 1, 1),
            (4021, 1, 1),
            (4030, 1, 1),
            (4040, 1, 1),
            (4050, 1, 1),
            (4070, 1, 1),
            (4060, 1, 1),
            (4090, 1, 1),
            (4080, 1, 1),
            (4120, 1, 1),
            (4100, 1, 1),
            (4110, 1, 1),
            (4460, 1, 1),
            (4470, 1, 1),
            (4140, 1, 1),
            (4130, 1, 1),
            (4630, 1, 1),
            (4200, 1, 1),
            (4220, 1, 1),
            (4210, 1, 1),
            (4390, 1, 1),
            (4300, 1, 1),
            (4301, 1, 1),
            (4302, 1, 1),
            (4370, 1, 1),
            (4380, 1, 1),
            (4381, 1, 1),
            (4361, 1, 1),
            (4440, 1, 1),
            (4430, 1, 1),
            (4450, 1, 1),
            (4431, 1, 1),
            (4480, 1, 1),
            (4640, 1, 1),
            (4600, 1, 1),
            (4610, 1, 1),
            (4620, 1, 1),
            (6500, 1, 1),
            (4660, 1, 1),
            (4670, 1, 1),
            (4650, 1, 1),
            (4800, 1, 1),
            (4820, 1, 1),
            (4810, 1, 1),
            (4400, 1, 1),
            (4420, 1, 1),
            (4490, 1, 1),
            (4410, 1, 1),
            (4500, 1, 1),
            (4520, 1, 1),
            (4510, 1, 1),
            (4720, 1, 1),
            (4721, 1, 1),
            (4710, 1, 1),
            (4700, 1, 1),
            (4701, 1, 1),
            (5100, 1, 1),
            (5110, 1, 1),
            (4900, 1, 1),
            (4910, 1, 1),
            (5000, 1, 1),
            (5001, 1, 1),
            (5010, 1, 1),
            (5020, 1, 1),
            (5030, 1, 1),
            (6420, 1, 1),
            (6421, 1, 1),
            (6422, 1, 1),
            (6423, 1, 1),
            (6440, 1, 1),
            (6441, 1, 1),
            (6460, 1, 1),
            (6450, 1, 1),
            (6470, 1, 1),
            (6480, 1, 1),
            (6490, 1, 1),
            (6400, 1, 1),
            (6520, 1, 1),
            (6530, 1, 1),
            (6510, 1, 1),
            (6600, 1, 1),
            (6330, 1, 1),
            (7903, 1, 1),
            (6340, 1, 1),
            (6410, 1, 1),
            (6424, 1, 1),
            (6430, 1, 1),
            (6431, 1, 1),
            (7520, 1, 1),
            (7500, 1, 1),
            (7510, 1, 1),
            (6720, 1, 1),
            (6700, 1, 1),
            (6701, 1, 1),
            (6710, 1, 1),
            (6750, 1, 1),
            (6780, 1, 1),
            (6770, 1, 1),
            (6740, 1, 1),
            (6760, 1, 1),
            (6730, 1, 1),
            (6900, 1, 1),
            (6920, 1, 1),
            (6930, 1, 1),
            (6940, 1, 1),
            (6910, 1, 1),
            (6950, 1, 1),
            (6921, 1, 1),
            (6960, 1, 1),
            (6971, 1, 1),
            (6970, 1, 1),
            (6000, 1, 1),
            (6001, 1, 1),
            (6010, 1, 1),
            (6100, 1, 1),
            (6020, 1, 1),
            (6110, 1, 1),
            (6220, 1, 1),
            (6030, 1, 1),
            (6040, 1, 1),
            (6060, 1, 1),
            (6050, 1, 1),
            (7900, 1, 1),
            (6210, 1, 1),
            (6230, 1, 1),
            (6240, 1, 1),
            (6250, 1, 1),
            (6260, 1, 1),
            (6270, 1, 1),
            (6820, 1, 1),
            (6830, 1, 1),
            (6800, 1, 1),
            (6810, 1, 1),
            (6850, 1, 1),
            (6840, 1, 1),
            (7210, 1, 1),
            (6300, 1, 1),
            (6320, 1, 1),
            (7220, 1, 1),
            (7200, 1, 1),
            (7230, 1, 1),
            (7310, 1, 1),
            (7311, 1, 1),
            (7320, 1, 1),
            (7330, 1, 1),
            (7300, 1, 1),
            (7000, 1, 1),
            (7001, 1, 1),
            (7010, 1, 1),
            (7011, 1, 1),
            (7040, 1, 1),
            (7041, 1, 1),
            (7030, 1, 1),
            (7031, 1, 1),
            (7020, 1, 1),
            (7021, 1, 1),
            (7060, 1, 1),
            (7080, 1, 1),
            (7090, 1, 1),

            // Spirit Ashes
            (240000, 1, 1),
            (241000, 1, 1),
            (205000, 1, 1),
            (215000, 1, 1),
            (212000, 1, 1),
            (213000, 1, 1),
            (211000, 1, 1),
            (210000, 1, 1),
            (234000, 1, 1),
            (222000, 1, 1),
            (214000, 1, 1),
            (232000, 1, 1),
            (235000, 1, 1),
            (233000, 1, 1),
            (237000, 1, 1),
            (244000, 1, 1),
            (236000, 1, 1),
            (245000, 1, 1),
            (203000, 1, 1),
            (246000, 1, 1),
            (249000, 1, 1),
            (248000, 1, 1),
            (220000, 1, 1),
            (242000, 1, 1),
            (225000, 1, 1),
            (226000, 1, 1),
            (231000, 1, 1),
            (243000, 1, 1),
            (227000, 1, 1),
            (209000, 1, 1),
            (230000, 1, 1),
            (229000, 1, 1),
            (208000, 1, 1),
            (224000, 1, 1),
            (218000, 1, 1),
            (219000, 1, 1),
            (250000, 1, 1),
            (251000, 1, 1),
            (252000, 1, 1),
            (253000, 1, 1),
            (255000, 1, 1),
            (254000, 1, 1),
            (238000, 1, 1),
            (201000, 1, 1),
            (202000, 1, 1),
            (239000, 1, 1),
            (221000, 1, 1),
            (204000, 1, 1),
            (217000, 1, 1),
            (216000, 1, 1),
            (247000, 1, 1),
            (228000, 1, 1),
            (256000, 1, 1),
            (257000, 1, 1),
            (258000, 1, 1),
            (223000, 1, 1),
            (200000, 1, 1),
            (207000, 1, 1),
            (261000, 1, 1),
            (263000, 1, 1),
            (262000, 1, 1),
            (259000, 1, 1),
            (260000, 1, 1),
            (206000, 1, 1),

            // Ashes of war
            (10600, 5, 1),
            (10700, 5, 1),
            (11000, 5, 1),
            (10000, 5, 1),
            (60700, 5, 1),
            (50300, 5, 1),
            (70000, 5, 1),
            (50600, 5, 1),
            (21200, 5, 1),
            (65100, 5, 1),
            (65000, 5, 1),
            (65400, 5, 1),
            (65300, 5, 1),
            (10300, 5, 1),
            (10100, 5, 1),
            (10200, 5, 1),
            (10900, 5, 1),
            (11200, 5, 1),
            (12400, 5, 1),
            (11400, 5, 1),
            (80000, 5, 1),
            (80100, 5, 1),
            (80200, 5, 1),
            (65200, 5, 1),
            (11500, 5, 1),
            (10500, 5, 1),
            (11100, 5, 1),
            (11600, 5, 1),
            (21000, 5, 1),
            (12200, 5, 1),
            (12300, 5, 1),
            (50200, 5, 1),
            (22000, 5, 1),
            (22500, 5, 1),
            (60000, 5, 1),
            (60100, 5, 1),
            (20300, 5, 1),
            (20000, 5, 1),
            (21900, 5, 1),
            (21800, 5, 1),
            (12000, 5, 1),
            (11800, 5, 1),
            (20900, 5, 1),
            (21400, 5, 1),
            (50500, 5, 1),
            (20700, 5, 1),
            (11300, 5, 1),
            (22100, 5, 1),
            (21600, 5, 1),
            (21700, 5, 1),
            (50400, 5, 1),
            (20100, 5, 1),
            (20800, 5, 1),
            (22200, 5, 1),
            (60400, 5, 1),
            (60500, 5, 1),
            (21300, 5, 1),
            (50700, 5, 1),
            (60300, 5, 1),
            (70100, 5, 1),
            (70200, 5, 1),
            (22800, 5, 1),
            (11900, 5, 1),
            (22400, 5, 1),
            (20400, 5, 1),
            (10800, 5, 1),
            (60600, 5, 1),
            (20200, 5, 1),
            (22700, 5, 1),
            (50100, 5, 1),
            (22600, 5, 1),
            (20500, 5, 1),
            (85000, 5, 1),
            (60200, 5, 1),
            (40200, 5, 1),
            (40000, 5, 1),
            (40100, 5, 1),
            (40500, 5, 1),
            (40400, 5, 1),
            (40600, 5, 1),
            (30200, 5, 1),
            (30700, 5, 1),
            (30600, 5, 1),
            (30000, 5, 1),
            (30800, 5, 1),
            (30100, 5, 1),
            (31000, 5, 1),
            (30500, 5, 1),
            (30900, 5, 1),
            
            // Arrows
            (50000000, 2, 99),
            (50010000, 2, 99),
            (50020000, 2, 99),
            (50030000, 2, 99),
            (50040000, 2, 99),
            (50060000, 2, 99),
            (50080000, 2, 99),
            (50090000, 2, 99),
            (50100000, 2, 99),
            (50110000, 2, 99),
            (50130000, 2, 99),
            (50140000, 2, 99),
            (50150000, 2, 99),
            (50160000, 2, 99),
            (50170000, 2, 99),
            (50180000, 2, 99),
            (50190000, 2, 99),
            (50200000, 2, 99),
            (50210000, 2, 99),
            (50220000, 2, 99),
            (50230000, 2, 99),
            (50240000, 2, 99),
            (50250000, 2, 99),
            (50260000, 2, 99),
            (50270000, 2, 99),
            (50280000, 2, 99),
            (50290000, 2, 99),
            (50300000, 2, 99),
            (50310000, 2, 99),
            (50320000, 2, 99),
            (50330000, 2, 99),
            (50340000, 2, 99),
            (50350000, 2, 99),
            (51000000, 2, 99),
            (51010000, 2, 99),
            (51020000, 2, 99),
            (51030000, 2, 99),
            (51040000, 2, 99),
            (51050000, 2, 99),
            (51060000, 2, 99),
            (52000000, 2, 99),
            (52010000, 2, 99),
            (52020000, 2, 99),
            (52030000, 2, 99),
            (52040000, 2, 99),
            (52050000, 2, 99),
            (52060000, 2, 99),
            (52070000, 2, 99),
            (52080000, 2, 99),
            (52090000, 2, 99),
            (52100000, 2, 99),
            (52110000, 2, 99),
            (52120000, 2, 99),
            (52130000, 2, 99),
            (52140000, 2, 99),
            (52150000, 2, 99),
            (52160000, 2, 99),
            (52170000, 2, 99),
            (52180000, 2, 99),
            (52190000, 2, 99),
            (53000000, 2, 99),
            (53010000, 2, 99),
            (53020000, 2, 99),
            (53030000, 2, 99),

            // Weapons
            (1000000, 2, 1),
            (1020000, 2, 1),
            (1030000, 2, 1),
            (1090000, 2, 1),
            (1140000, 2, 1),
            (1150000, 2, 1),
            (1100000, 2, 1),
            (1060000, 2, 1),
            (1130000, 2, 1),
            (1050000, 2, 1),
            (1080000, 2, 1),
            (1110000, 2, 1),
            (1070000, 2, 1),
            (1040000, 2, 1),
            (1160000, 2, 1),
            (1010000, 2, 1),
            (2010000, 2, 1),
            (2000000, 2, 1),
            (2020000, 2, 1),
            (2050000, 2, 1),
            (2040000, 2, 1),
            (2230000, 2, 1),
            (2210000, 2, 1),
            (2240000, 2, 1),
            (2250000, 2, 1),
            (2180000, 2, 1),
            (2150000, 2, 1),
            (2260000, 2, 1),
            (2200000, 2, 1),
            (2060000, 2, 1),
            (2070000, 2, 1),
            (2190000, 2, 1),
            (2220000, 2, 1),
            (2110000, 2, 1),
            (2140000, 2, 1),
            (3000000, 2, 1),
            (3180000, 2, 1),
            (3020000, 2, 1),
            (3030000, 2, 1),
            (3040000, 2, 1),
            (3080000, 2, 1),
            (3010000, 2, 1),
            (3050000, 2, 1),
            (3190000, 2, 1),
            (3210000, 2, 1),
            (2090000, 2, 1),
            (3160000, 2, 1),
            (3150000, 2, 1),
            (3060000, 2, 1),
            (3070000, 2, 1),
            (3200000, 2, 1),
            (3130000, 2, 1),
            (3170000, 2, 1),
            (3090000, 2, 1),
            (4040000, 2, 1),
            (4000000, 2, 1),
            (4010000, 2, 1),
            (4030000, 2, 1),
            (4110000, 2, 1),
            (4060000, 2, 1),
            (4100000, 2, 1),
            (4080000, 2, 1),
            (4070000, 2, 1),
            (5020000, 2, 1),
            (5000000, 2, 1),
            (5060000, 2, 1),
            (5010000, 2, 1),
            (5030000, 2, 1),
            (5040000, 2, 1),
            (5050000, 2, 1),
            (6020000, 2, 1),
            (6010000, 2, 1),
            (6000000, 2, 1),
            (7140000, 2, 1),
            (7000000, 2, 1),
            (7030000, 2, 1),
            (7150000, 2, 1),
            (7040000, 2, 1),
            (7020000, 2, 1),
            (7080000, 2, 1),
            (7120000, 2, 1),
            (7010000, 2, 1),
            (7060000, 2, 1),
            (7110000, 2, 1),
            (7050000, 2, 1),
            (2080000, 2, 1),
            (7070000, 2, 1),
            (7100000, 2, 1),
            (8020000, 2, 1),
            (8060000, 2, 1),
            (8070000, 2, 1),
            (8080000, 2, 1),
            (8030000, 2, 1),
            (8010000, 2, 1),
            (8050000, 2, 1),
            (8040000, 2, 1),
            (9000000, 2, 1),
            (9010000, 2, 1),
            (9080000, 2, 1),
            (9030000, 2, 1),
            (9060000, 2, 1),
            (9040000, 2, 1),
            (9070000, 2, 1),
            (10000000, 2, 1),
            (10030000, 2, 1),
            (10010000, 2, 1),
            (10080000, 2, 1),
            (10090000, 2, 1),
            (10050000, 2, 1),
            (14010000, 2, 1),
            (14020000, 2, 1),
            (14000000, 2, 1),
            (15010000, 2, 1),
            (14030000, 2, 1),
            (14040000, 2, 1),
            (14100000, 2, 1),
            (14060000, 2, 1),
            (14110000, 2, 1),
            (14080000, 2, 1),
            (14050000, 2, 1),
            (14140000, 2, 1),
            (14120000, 2, 1),
            (15000000, 2, 1),
            (15030000, 2, 1),
            (15050000, 2, 1),
            (15080000, 2, 1),
            (15020000, 2, 1),
            (15060000, 2, 1),
            (15120000, 2, 1),
            (15130000, 2, 1),
            (15140000, 2, 1),
            (11010000, 2, 1),
            (11030000, 2, 1),
            (11070000, 2, 1),
            (11140000, 2, 1),
            (11000000, 2, 1),
            (11050000, 2, 1),
            (11040000, 2, 1),
            (11080000, 2, 1),
            (11090000, 2, 1),
            (11060000, 2, 1),
            (11100000, 2, 1),
            (11120000, 2, 1),
            (11130000, 2, 1),
            (11110000, 2, 1),
            (12000000, 2, 1),
            (12080000, 2, 1),
            (12060000, 2, 1),
            (12140000, 2, 1),
            (12190000, 2, 1),
            (12020000, 2, 1),
            (12210000, 2, 1),
            (12130000, 2, 1),
            (12180000, 2, 1),
            (12010000, 2, 1),
            (12160000, 2, 1),
            (12170000, 2, 1),
            (12150000, 2, 1),
            (12200000, 2, 1),
            (13010000, 2, 1),
            (13000000, 2, 1),
            (13040000, 2, 1),
            (13020000, 2, 1),
            (23040000, 2, 1),
            (23150000, 2, 1),
            (23120000, 2, 1),
            (23110000, 2, 1),
            (23000000, 2, 1),
            (23020000, 2, 1),
            (23130000, 2, 1),
            (23060000, 2, 1),
            (23010000, 2, 1),
            (23070000, 2, 1),
            (23140000, 2, 1),
            (23030000, 2, 1),
            (23100000, 2, 1),
            (23080000, 2, 1),
            (16000000, 2, 1),
            (16150000, 2, 1),
            (16010000, 2, 1),
            (16050000, 2, 1),
            (16070000, 2, 1),
            (16140000, 2, 1),
            (16110000, 2, 1),
            (16030000, 2, 1),
            (16060000, 2, 1),
            (16080000, 2, 1),
            (16130000, 2, 1),
            (16020000, 2, 1),
            (16160000, 2, 1),
            (16040000, 2, 1),
            (16120000, 2, 1),
            (16090000, 2, 1),
            (17060000, 2, 1),
            (17070000, 2, 1),
            (17030000, 2, 1),
            (17020000, 2, 1),
            (17050000, 2, 1),
            (18000000, 2, 1),
            (18030000, 2, 1),
            (18020000, 2, 1),
            (18090000, 2, 1),
            (18130000, 2, 1),
            (18070000, 2, 1),
            (18110000, 2, 1),
            (18150000, 2, 1),
            (18160000, 2, 1),
            (18050000, 2, 1),
            (18010000, 2, 1),
            (18060000, 2, 1),
            (18080000, 2, 1),
            (18140000, 2, 1),
            (18100000, 2, 1),
            (18040000, 2, 1),
            (19000000, 2, 1),
            (19010000, 2, 1),
            (19020000, 2, 1),
            (19060000, 2, 1),
            (20000000, 2, 1),
            (20020000, 2, 1),
            (20070000, 2, 1),
            (20050000, 2, 1),
            (20030000, 2, 1),
            (21000000, 2, 1),
            (21010000, 2, 1),
            (21100000, 2, 1),
            (21070000, 2, 1),
            (21080000, 2, 1),
            (21110000, 2, 1),
            (21120000, 2, 1),
            (21130000, 2, 1),
            (22000000, 2, 1),
            (22010000, 2, 1),
            (22020000, 2, 1),
            (22030000, 2, 1),
            (40000000, 2, 1),
            (40050000, 2, 1),
            (40020000, 2, 1),
            (40010000, 2, 1),
            (40030000, 2, 1),
            (41000000, 2, 1),
            (41010000, 2, 1),
            (41070000, 2, 1),
            (41060000, 2, 1),
            (41020000, 2, 1),
            (41040000, 2, 1),
            (41030000, 2, 1),
            (42040000, 2, 1),
            (42010000, 2, 1),
            (42030000, 2, 1),
            (43000000, 2, 1),
            (43020000, 2, 1),
            (43030000, 2, 1),
            (43080000, 2, 1),
            (43110000, 2, 1),
            (43050000, 2, 1),
            (43060000, 2, 1),
            (44000000, 2, 1),
            (44010000, 2, 1),
            (33130000, 2, 1),
            (33000000, 2, 1),
            (33200000, 2, 1),
            (33120000, 2, 1),
            (33060000, 2, 1),
            (33230000, 2, 1),
            (33240000, 2, 1),
            (33210000, 2, 1),
            (33170000, 2, 1),
            (33190000, 2, 1),
            (33280000, 2, 1),
            (33050000, 2, 1),
            (33040000, 2, 1),
            (33270000, 2, 1),
            (33250000, 2, 1),
            (33260000, 2, 1),
            (33180000, 2, 1),
            (34000000, 2, 1),
            (34070000, 2, 1),
            (34060000, 2, 1),
            (34030000, 2, 1),
            (34020000, 2, 1),
            (34010000, 2, 1),
            (34040000, 2, 1),
            (34090000, 2, 1),
            (34080000, 2, 1),
            (24000000, 2, 1),
            (24060000, 2, 1),
            (24020000, 2, 1),
            (24070000, 2, 1),
            (24050000, 2, 1),
            (24040000, 2, 1),
            (30030000, 2, 1),
            (30090000, 2, 1),
            (30100000, 2, 1),
            (30080000, 2, 1),
            (30070000, 2, 1),
            (30040000, 2, 1),
            (30000000, 2, 1),
            (30120000, 2, 1),
            (30130000, 2, 1),
            (30020000, 2, 1),
            (30140000, 2, 1),
            (30110000, 2, 1),
            (30010000, 2, 1),
            (31170000, 2, 1),
            (30190000, 2, 1),
            (30150000, 2, 1),
            (30200000, 2, 1),
            (31270000, 2, 1),
            (31240000, 2, 1),
            (31250000, 2, 1),
            (31260000, 2, 1),
            (31020000, 2, 1),
            (31050000, 2, 1),
            (31070000, 2, 1),
            (31230000, 2, 1),
            (31340000, 2, 1),
            (31010000, 2, 1),
            (31330000, 2, 1),
            (31300000, 2, 1),
            (31290000, 2, 1),
            (31280000, 2, 1),
            (31320000, 2, 1),
            (31310000, 2, 1),
            (31000000, 2, 1),
            (31100000, 2, 1),
            (31030000, 2, 1),
            (31080000, 2, 1),
            (31090000, 2, 1),
            (31130000, 2, 1),
            (31040000, 2, 1),
            (30060000, 2, 1),
            (31190000, 2, 1),
            (31060000, 2, 1),
            (31140000, 2, 1),
            (32290000, 2, 1),
            (32300000, 2, 1),
            (32050000, 2, 1),
            (32170000, 2, 1),
            (32140000, 2, 1),
            (32090000, 2, 1),
            (32190000, 2, 1),
            (32200000, 2, 1),
            (32210000, 2, 1),
            (32000000, 2, 1),
            (32020000, 2, 1),
            (32270000, 2, 1),
            (32250000, 2, 1),
            (32230000, 2, 1),
            (32260000, 2, 1),
            (32280000, 2, 1),
            (32030000, 2, 1),
            (32040000, 2, 1),
            (32130000, 2, 1),
            (32240000, 2, 1),
            (32220000, 2, 1),
            (32080000, 2, 1),
            (32120000, 2, 1),
            (32160000, 2, 1),
            (32150000, 2, 1),

            // Protectors
            (1020100, 3, 1),
            (1020200, 3, 1),
            (1020300, 3, 1),
            (810000, 3, 1),
            (811000, 3, 1),
            (811100, 3, 1),
            (812100, 3, 1),
            (810300, 3, 1),
            (370000, 3, 1),
            (380000, 3, 1),
            (370100, 3, 1),
            (371100, 3, 1),
            (380100, 3, 1),
            (370300, 3, 1),
            (390000, 3, 1),
            (390100, 3, 1),
            (390300, 3, 1),
            (220000, 3, 1),
            (221100, 3, 1),
            (220300, 3, 1),
            (1990000, 3, 1),
            (1991100, 3, 1),
            (330000, 3, 1),
            (331100, 3, 1),
            (330100, 3, 1),
            (330200, 3, 1),
            (330300, 3, 1),
            (801000, 3, 1),
            (800100, 3, 1),
            (801100, 3, 1),
            (802000, 3, 1),
            (802100, 3, 1),
            (160000, 3, 1),
            (160100, 3, 1),
            (160300, 3, 1),
            (890000, 3, 1),
            (890100, 3, 1),
            (890300, 3, 1),
            (891000, 3, 1),
            (964000, 3, 1),
            (964100, 3, 1),
            (963200, 3, 1),
            (2020000, 3, 1),
            (1130000, 3, 1),
            (2010000, 3, 1),
            (1130100, 3, 1),
            (1130200, 3, 1),
            (1130300, 3, 1),
            (630000, 3, 1),
            (631100, 3, 1),
            (630200, 3, 1),
            (630300, 3, 1),
            (1030000, 3, 1),
            (1030100, 3, 1),
            (833000, 3, 1),
            (831000, 3, 1),
            (830000, 3, 1),
            (834000, 3, 1),
            (830100, 3, 1),
            (830200, 3, 1),
            (830300, 3, 1),
            (832000, 3, 1),
            (2030000, 3, 1),
            (1000000, 3, 1),
            (1000100, 3, 1),
            (1000200, 3, 1),
            (1000300, 3, 1),
            (990000, 3, 1),
            (991100, 3, 1),
            (990200, 3, 1),
            (990300, 3, 1),
            (130000, 3, 1),
            (131100, 3, 1),
            (130200, 3, 1),
            (130300, 3, 1),
            (121000, 3, 1),
            (121100, 3, 1),
            (120200, 3, 1),
            (120300, 3, 1),
            (910000, 3, 1),
            (911100, 3, 1),
            (910200, 3, 1),
            (910300, 3, 1),
            (911000, 3, 1),
            (581000, 3, 1),
            (581100, 3, 1),
            (581200, 3, 1),
            (580000, 3, 1),
            (580100, 3, 1),
            (580200, 3, 1),
            (580300, 3, 1),
            (510000, 3, 1),
            (510100, 3, 1),
            (510200, 3, 1),
            (510300, 3, 1),
            (1010000, 3, 1),
            (1011100, 3, 1),
            (1010300, 3, 1),
            (1940000, 3, 1),
            (1941100, 3, 1),
            (1930100, 3, 1),
            (620000, 3, 1),
            (622100, 3, 1),
            (620300, 3, 1),
            (620100, 3, 1),
            (900000, 3, 1),
            (901100, 3, 1),
            (900200, 3, 1),
            (900300, 3, 1),
            (902000, 3, 1),
            (903100, 3, 1),
            (902300, 3, 1),
            (430000, 3, 1),
            (430100, 3, 1),
            (430300, 3, 1),
            (1600000, 3, 1),
            (1040000, 3, 1),
            (1040100, 3, 1),
            (1040200, 3, 1),
            (1040300, 3, 1),
            (90000, 3, 1),
            (91100, 3, 1),
            (90200, 3, 1),
            (90300, 3, 1),
            (100000, 3, 1),
            (101100, 3, 1),
            (100200, 3, 1),
            (100300, 3, 1),
            (540000, 3, 1),
            (541100, 3, 1),
            (540200, 3, 1),
            (540300, 3, 1),
            (962100, 3, 1),
            (961000, 3, 1),
            (961100, 3, 1),
            (960000, 3, 1),
            (960100, 3, 1),
            (960300, 3, 1),
            (963000, 3, 1),
            (963100, 3, 1),
            (300000, 3, 1),
            (300100, 3, 1),
            (300300, 3, 1),
            (301000, 3, 1),
            (301100, 3, 1),
            (301300, 3, 1),
            (520000, 3, 1),
            (520100, 3, 1),
            (520200, 3, 1),
            (520300, 3, 1),
            (530000, 3, 1),
            (530100, 3, 1),
            (530200, 3, 1),
            (530300, 3, 1),
            (1050100, 3, 1),
            (320000, 3, 1),
            (320100, 3, 1),
            (320300, 3, 1),
            (481100, 3, 1),
            (1910000, 3, 1),
            (1920000, 3, 1),
            (1900000, 3, 1),
            (1902000, 3, 1),
            (1901000, 3, 1),
            (1080000, 3, 1),
            (1084000, 3, 1),
            (1081000, 3, 1),
            (1082000, 3, 1),
            (1083000, 3, 1),
            (1085000, 3, 1),
            (1300000, 3, 1),
            (1301000, 3, 1),
            (1090000, 3, 1),
            (820000, 3, 1),
            (1110000, 3, 1),
            (1120000, 3, 1),
            (1060000, 3, 1),
            (1060100, 3, 1),
            (1400000, 3, 1),
            (1400100, 3, 1),
            (1400200, 3, 1),
            (1400300, 3, 1),
            (670000, 3, 1),
            (670100, 3, 1),
            (670200, 3, 1),
            (670300, 3, 1),
            (740000, 3, 1),
            (741000, 3, 1),
            (740100, 3, 1),
            (740200, 3, 1),
            (740300, 3, 1),
            (680000, 3, 1),
            (681100, 3, 1),
            (680200, 3, 1),
            (680300, 3, 1),
            (250000, 3, 1),
            (251100, 3, 1),
            (250300, 3, 1),
            (1401000, 3, 1),
            (931100, 3, 1),
            (930200, 3, 1),
            (930300, 3, 1),
            (881000, 3, 1),
            (881100, 3, 1),
            (880200, 3, 1),
            (880300, 3, 1),
            (1890000, 3, 1),
            (1890100, 3, 1),
            (1890200, 3, 1),
            (1890300, 3, 1),
            (930000, 3, 1),
            (930100, 3, 1),
            (1830000, 3, 1),
            (1830100, 3, 1),
            (1830200, 3, 1),
            (1830300, 3, 1),
            (1840000, 3, 1),
            (1840100, 3, 1),
            (1860000, 3, 1),
            (1860100, 3, 1),
            (1850000, 3, 1),
            (1850100, 3, 1),
            (1880000, 3, 1),
            (1880100, 3, 1),
            (1870100, 3, 1),
            (1980000, 3, 1),
            (1980100, 3, 1),
            (1980200, 3, 1),
            (420000, 3, 1),
            (420100, 3, 1),
            (420200, 3, 1),
            (420300, 3, 1),
            (310000, 3, 1),
            (311100, 3, 1),
            (310300, 3, 1),
            (2000000, 3, 1),
            (2001100, 3, 1),
            (2000300, 3, 1),
            (291000, 3, 1),
            (291100, 3, 1),
            (290200, 3, 1),
            (290300, 3, 1),
            (294000, 3, 1),
            (294100, 3, 1),
            (293000, 3, 1),
            (293100, 3, 1),
            (730000, 3, 1),
            (730100, 3, 1),
            (730200, 3, 1),
            (730300, 3, 1),
            (1100000, 3, 1),
            (1100100, 3, 1),
            (1102100, 3, 1),
            (1101100, 3, 1),
            (1100200, 3, 1),
            (1100300, 3, 1),
            (40000, 3, 1),
            (40100, 3, 1),
            (40200, 3, 1),
            (40300, 3, 1),
            (1700000, 3, 1),
            (1700100, 3, 1),
            (1700200, 3, 1),
            (1700300, 3, 1),
            (1710000, 3, 1),
            (1710100, 3, 1),
            (1710200, 3, 1),
            (1710300, 3, 1),
            (1730000, 3, 1),
            (1730100, 3, 1),
            (1730200, 3, 1),
            (1730300, 3, 1),
            (1720000, 3, 1),
            (1720100, 3, 1),
            (1720200, 3, 1),
            (1720300, 3, 1),
            (1750000, 3, 1),
            (1750100, 3, 1),
            (1750200, 3, 1),
            (1750300, 3, 1),
            (1740100, 3, 1),
            (1740200, 3, 1),
            (1740300, 3, 1),
            (190000, 3, 1),
            (190100, 3, 1),
            (190200, 3, 1),
            (190300, 3, 1),
            (50000, 3, 1),
            (50100, 3, 1),
            (50200, 3, 1),
            (50300, 3, 1),
            (870000, 3, 1),
            (871100, 3, 1),
            (870200, 3, 1),
            (870300, 3, 1),
            (872000, 3, 1),
            (872100, 3, 1),
            (872200, 3, 1),
            (872300, 3, 1),
            (150000, 3, 1),
            (151100, 3, 1),
            (150200, 3, 1),
            (150300, 3, 1),
            (941000, 3, 1),
            (940100, 3, 1),
            (940200, 3, 1),
            (940300, 3, 1),
            (840000, 3, 1),
            (850000, 3, 1),
            (840100, 3, 1),
            (240000, 3, 1),
            (241100, 3, 1),
            (240200, 3, 1),
            (240300, 3, 1),
            (350000, 3, 1),
            (350100, 3, 1),
            (350200, 3, 1),
            (350300, 3, 1),
            (351000, 3, 1),
            (351100, 3, 1),
            (351200, 3, 1),
            (351300, 3, 1),
            (1070000, 3, 1),
            (1070100, 3, 1),
            (1070200, 3, 1),
            (1070300, 3, 1),
            (180000, 3, 1),
            (181100, 3, 1),
            (180200, 3, 1),
            (180300, 3, 1),
            (770000, 3, 1),
            (771100, 3, 1),
            (770200, 3, 1),
            (770300, 3, 1),
            (460000, 3, 1),
            (461100, 3, 1),
            (460200, 3, 1),
            (460300, 3, 1),
            (1500000, 3, 1),
            (1500100, 3, 1),
            (1500200, 3, 1),
            (1500300, 3, 1),
            (660000, 3, 1),
            (661100, 3, 1),
            (660200, 3, 1),
            (660300, 3, 1),
            (1101000, 3, 1),
            (980000, 3, 1),
            (981100, 3, 1),
            (980200, 3, 1),
            (980300, 3, 1),
            (1770000, 3, 1),
            (1771100, 3, 1),
            (1770200, 3, 1),
            (1770300, 3, 1),
            (1780000, 3, 1),
            (1781100, 3, 1),
            (1780200, 3, 1),
            (1780300, 3, 1),
            (1800000, 3, 1),
            (1801100, 3, 1),
            (1800200, 3, 1),
            (1800300, 3, 1),
            (1760000, 3, 1),
            (1761100, 3, 1),
            (1760200, 3, 1),
            (1760300, 3, 1),
            (1790000, 3, 1),
            (1791100, 3, 1),
            (1790200, 3, 1),
            (1790300, 3, 1),
            (1820000, 3, 1),
            (1821100, 3, 1),
            (1820200, 3, 1),
            (1820300, 3, 1),
            (1811100, 3, 1),
            (1810200, 3, 1),
            (1810300, 3, 1),
            (790000, 3, 1),
            (791100, 3, 1),
            (790200, 3, 1),
            (790300, 3, 1),
            (341000, 3, 1),
            (341100, 3, 1),
            (340200, 3, 1),
            (340300, 3, 1),
            (860000, 3, 1),
            (861100, 3, 1),
            (860200, 3, 1),
            (860300, 3, 1),
            (650000, 3, 1),
            (651000, 3, 1),
            (652100, 3, 1),
            (650200, 3, 1),
            (650300, 3, 1),
            (600000, 3, 1),
            (601100, 3, 1),
            (600200, 3, 1),
            (601300, 3, 1),
            (61000, 3, 1),
            (61100, 3, 1),
            (60200, 3, 1),
            (60300, 3, 1),
            (170000, 3, 1),
            (171100, 3, 1),
            (170200, 3, 1),
            (170300, 3, 1),
            (210000, 3, 1),
            (211100, 3, 1),
            (210200, 3, 1),
            (210300, 3, 1),
            (950000, 3, 1),
            (951100, 3, 1),
            (950200, 3, 1),
            (950300, 3, 1),
            (690000, 3, 1),
            (690100, 3, 1),
            (690200, 3, 1),
            (690300, 3, 1),
            (590000, 3, 1),
            (591100, 3, 1),
            (590200, 3, 1),
            (590300, 3, 1),
            (280000, 3, 1),
            (281100, 3, 1),
            (280200, 3, 1),
            (280300, 3, 1),
            (760000, 3, 1),
            (761100, 3, 1),
            (760200, 3, 1),
            (760300, 3, 1),
            (200000, 3, 1),
            (201000, 3, 1),
            (200100, 3, 1),
            (201100, 3, 1),
            (200200, 3, 1),
            (200300, 3, 1),
            (231000, 3, 1),
            (231100, 3, 1),
            (230200, 3, 1),
            (230300, 3, 1),
            (780000, 3, 1),
            (781100, 3, 1),
            (780200, 3, 1),
            (780300, 3, 1),
            (80000, 3, 1),
            (81100, 3, 1),
            (80200, 3, 1),
            (80300, 3, 1),
            (720000, 3, 1),
            (721100, 3, 1),
            (720200, 3, 1),
            (720300, 3, 1),
            (270000, 3, 1),
            (271100, 3, 1),
            (270200, 3, 1),
            (270300, 3, 1),
            (260000, 3, 1),
            (260100, 3, 1),
            (260200, 3, 1),
            (260300, 3, 1),
            (570000, 3, 1),
            (572100, 3, 1),
            (570200, 3, 1),
            (570300, 3, 1),
            (571000, 3, 1),
            (573100, 3, 1),
            (470000, 3, 1),
            (471100, 3, 1),
            (470200, 3, 1),
            (470300, 3, 1),
            (640000, 3, 1),
            (641100, 3, 1),
            (640200, 3, 1),
            (640300, 3, 1),
            (140000, 3, 1),
            (140100, 3, 1),
            (140200, 3, 1),
            (140300, 3, 1),
            (970000, 3, 1),
            (970100, 3, 1),
            (970200, 3, 1),
            (970300, 3, 1),
            (360000, 3, 1),
            (360100, 3, 1),
            (361100, 3, 1),
            (360200, 3, 1),
            (360300, 3, 1),
            (440000, 3, 1),

            // Accessories
            (1000, 4, 1),
            (1001, 4, 1),
            (1002, 4, 1),
            (5000, 4, 1),
            (5020, 4, 1),
            (1010, 4, 1),
            (1011, 4, 1),
            (1012, 4, 1),
            (5010, 4, 1),
            (1020, 4, 1),
            (1021, 4, 1),
            (1022, 4, 1),
            (1150, 4, 1),
            (1030, 4, 1),
            (1031, 4, 1),
            (1032, 4, 1),
            (1040, 4, 1),
            (1041, 4, 1),
            (1042, 4, 1),
            (1050, 4, 1),
            (1051, 4, 1),
            (1220, 4, 1),
            (1221, 4, 1),
            (1060, 4, 1),
            (1070, 4, 1),
            (1080, 4, 1),
            (1090, 4, 1),
            (4000, 4, 1),
            (4001, 4, 1),
            (4002, 4, 1),
            (4003, 4, 1),
            (4010, 4, 1),
            (4011, 4, 1),
            (4012, 4, 1),
            (4020, 4, 1),
            (4021, 4, 1),
            (4022, 4, 1),
            (4030, 4, 1),
            (4031, 4, 1),
            (4032, 4, 1),
            (4040, 4, 1),
            (4041, 4, 1),
            (4042, 4, 1),
            (4050, 4, 1),
            (4051, 4, 1),
            (4052, 4, 1),
            (1170, 4, 1),
            (1171, 4, 1),
            (1160, 4, 1),
            (1161, 4, 1),
            (1180, 4, 1),
            (1181, 4, 1),
            (1200, 4, 1),
            (1201, 4, 1),
            (1190, 4, 1),
            (1191, 4, 1),
            (2090, 4, 1),
            (2200, 4, 1),
            (2120, 4, 1),
            (2130, 4, 1),
            (2070, 4, 1),
            (2060, 4, 1),
            (2140, 4, 1),
            (2180, 4, 1),
            (4100, 4, 1),
            (2150, 4, 1),
            (2100, 4, 1),
            (3000, 4, 1),
            (3001, 4, 1),
            (3040, 4, 1),
            (3050, 4, 1),
            (3080, 4, 1),
            (1140, 4, 1),
            (3060, 4, 1),
            (3070, 4, 1),
            (2190, 4, 1),
            (2210, 4, 1),
            (2220, 4, 1),
            (6020, 4, 1),
            (1230, 4, 1),
            (1231, 4, 1),
            (3090, 4, 1),
            (1210, 4, 1),
            (2110, 4, 1),
            (2000, 4, 1),
            (2020, 4, 1),
            (2010, 4, 1),
            (2030, 4, 1),
            (4060, 4, 1),
            (4070, 4, 1),
            (4110, 4, 1),
            (2040, 4, 1),
            (4080, 4, 1),
            (2050, 4, 1),
            (4090, 4, 1),
            (5050, 4, 1),
            (5060, 4, 1),
            (2080, 4, 1),
            (2081, 4, 1),
            (1250, 4, 1),
            (5040, 4, 1),
            (2170, 4, 1),
            (2160, 4, 1),
            (5030, 4, 1),
            (1110, 4, 1),
            (1100, 4, 1),
            (6000, 4, 1),
            (6010, 4, 1),
            (6040, 4, 1),
            (6080, 4, 1),
            (6090, 4, 1),
            (6050, 4, 1),
            (6060, 4, 1),
        };
        EMEVD common = _resources.CommonEmevd;
        if (common == null)
        {
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

        foreach (var geasture in gestures)
        {
            newInstrs.Add(new EMEVD.Instruction(2003, 71, new List<object> { geasture }));
        }

        for (var i = 0; i < itemsToGive.Length; i++)
        {
            var (id, cat, count) = itemsToGive[i];
            addItemWithLotID(60000 + i, id, cat, count);
            if (i % 10 == 0)
            {
                // AwardItemLot(60000 + i)
                newInstrs.Add(new EMEVD.Instruction(2003, 4, new List<object> { 60000 + i }));
            }
        }

        int newEventId = 279551111; // Arbitrary number
        EMEVD.Event newEvent = new EMEVD.Event(newEventId, EMEVD.Event.RestBehaviorType.Default);
        newEvent.Instructions = newInstrs;
        common.Events.Add(newEvent);

        // Process to edit other events
        EMEVD.Event? constrEvent = common.Events.Find(e => e.ID == 0);
        EMEVD.Event? grantMapEvent = common.Events.Find(e => e.ID == 1600);
        EMEVD.Event? reachedAltusEvent = common.Events.Find(e => e.ID == 3044);
        if (constrEvent == null || grantMapEvent == null || reachedAltusEvent == null)
        {
            throw new InvalidOperationException($"{Const.CommonEventPath} missing one of required events [0, 1600, 3044]");
        }

        List<object> argsToSearch = [(byte)0, (uint)900, (byte)0];
        constrEvent.Instructions.RemoveAt(constrEvent.Instructions.FindIndex(instr =>
            instr.Bank == 2000 && instr.ID == 0 && instr.UnpackArgs([EMEVD.Instruction.ArgType.Byte, EMEVD.Instruction.ArgType.UInt32, EMEVD.Instruction.ArgType.Byte]).SequenceEqual(argsToSearch)));
        argsToSearch[1] = (uint)910;
        constrEvent.Instructions.RemoveAt(constrEvent.Instructions.FindIndex(instr =>
            instr.Bank == 2000 && instr.ID == 0 && instr.UnpackArgs([EMEVD.Instruction.ArgType.Byte, EMEVD.Instruction.ArgType.UInt32, EMEVD.Instruction.ArgType.Byte]).SequenceEqual(argsToSearch)));
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
        List<EMEVD.Instruction> rewriteInstrs = new()
        {
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

    private void patchAtkParam()
    {
        Param.Row swarmOfFlies1 = _resources.AtkParamPc[72100] ??
                                  throw new InvalidOperationException("Entry 72100 not found in AtkParam_Pc");
        Param.Row swarmOfFlies2 = _resources.AtkParamPc[72101] ??
                                  throw new InvalidOperationException("Entry 72101 not found in AtkParam_Pc");

        AtkParam swarmAtkParam1 = new(swarmOfFlies1);
        AtkParam swarmAtkParam2 = new(swarmOfFlies2);
        patchSpEffectAtkPowerCorrectRate(swarmAtkParam1);
        patchSpEffectAtkPowerCorrectRate(swarmAtkParam2);
    }

    private static void patchSpEffectAtkPowerCorrectRate(AtkParam atkParam)
    {
        atkParam.spEffectAtkPowerCorrectRate_byPoint = 100;
        atkParam.spEffectAtkPowerCorrectRate_byRate = 100;
        atkParam.spEffectAtkPowerCorrectRate_byDmg = 100;
    }
}