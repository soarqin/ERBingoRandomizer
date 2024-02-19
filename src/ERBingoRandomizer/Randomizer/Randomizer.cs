﻿using ERBingoRandomizer.FileHandler;
using ERBingoRandomizer.Params;
using ERBingoRandomizer.Utility;
using FSParam;
using SoulsFormats;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;


namespace ERBingoRandomizer.Randomizer;

public partial class BingoRandomizer {
    public SeedInfo SeedInfo { get; private set; }

    private readonly string _path;
    private readonly string _regulationPath;
    private BND4 _regulationBnd;
    private readonly string _seed;
    private int _seedInt;
    private readonly Random _random;
    private BHD5Reader _bhd5Reader;
    private IntPtr _oodlePtr;
    // FMGs
    private BND4[] _menuMsgBND = new BND4[14];
    private FMG[] _lineHelpFmg = new FMG[14];
    private FMG _menuTextFmg;
    private FMG[] _weaponFmg = new FMG[14];
    private FMG _protectorFmg;
    private FMG[] _goodsFmg = new FMG[14];
    // Params
    private List<PARAMDEF> _paramDefs;
    private Param _equipParamWeapon;
    private Param _equipParamCustomWeapon;
    private Param _equipParamGoods;
    private Param _equipParamProtector;
    private Param _charaInitParam;
    private Param _goodsParam;
    private Param _itemLotParam_map;
    private Param _itemLotParam_enemy;
    private Param _shopLineupParam;
    private Param _atkParam_Pc;
    // Dictionaries
    private Dictionary<int, EquipParamWeapon> _weaponDictionary;
    private Dictionary<int, EquipParamWeapon> _customWeaponDictionary;
    private Dictionary<int, string> _weaponNameDictionary;
    private Dictionary<int, EquipParamGoods> _goodsDictionary;
    private Dictionary<int, Magic> _magicDictionary;
    private Dictionary<ushort, List<Param.Row>> _weaponTypeDictionary;
    private Dictionary<byte, List<Param.Row>> _armorTypeDictionary;
    private Dictionary<byte, List<Param.Row>> _magicTypeDictionary;
    public Task RandomizeRegulation() {
        //calculateLevels();
        _randomizerLog = new List<string>();
        randomizeCharaInitParam();
        _cancellationToken.ThrowIfCancellationRequested();
        randomizeItemLotParams();
        _cancellationToken.ThrowIfCancellationRequested();
        randomizeShopLineupParam();
        _cancellationToken.ThrowIfCancellationRequested();
        randomizeShopLineupParamMagic();
        _cancellationToken.ThrowIfCancellationRequested();
        patchAtkParam();
        _cancellationToken.ThrowIfCancellationRequested();
        writeFiles();
        writeLog();
        SeedInfo = new SeedInfo(_seed,
            Util.GetShaRegulation256Hash());
        string seedJson = JsonSerializer.Serialize(SeedInfo);
        File.WriteAllText(Config.LastSeedPath, seedJson);
        return Task.CompletedTask;
    }
    private void randomizeCharaInitParam() {
        logItem(">> Class Randomization - All items are randomized, with each class having a .001% chance to gain or lose and item. Spells given class meets min stat requirements");
        logItem("> Ammo is give if you get a ranged weapon. Catalyst is give if you have spells.\n");
        IEnumerable<int> remembranceItems = _shopLineupParam.Rows.Where(r => r.ID is >= 101900 and <= 101929).Select(r => new ShopLineupParam(r).equipId);
        List<Param.Row> staves = _weaponTypeDictionary[Const.StaffType];
        List<Param.Row> seals = _weaponTypeDictionary[Const.SealType];
        List<int> weapons = _weaponDictionary.Keys.Select(removeWeaponMetadata).Distinct()
            .Where(id => remembranceItems.All(i => i != id))
            .Where(id => staves.All(s => s.ID != id) && seals.All(s => s.ID != id))
            .ToList();
        weapons.Shuffle(_random);

        List<int> spells = _magicDictionary.Keys.Select(id => id).Distinct()
            .Where(id => remembranceItems.All(r => r != id))
            .Where(id => staves.All(s => s.ID != id) && seals.All(s => s.ID != id)).ToList();
        spells.Shuffle(_random);

        for (int i = 0; i < 10; i++) {
            Param.Row? row = _charaInitParam[i + 3000];
            if (row == null) {
                continue;
            }
            CharaInitParam param = new(row);
            randomizeCharaInitEntry(param, weapons);
            guaranteeSpellcasters(row.ID, param, spells);
            logCharaInitEntry(param, i + 288100);
            addDescriptionString(param, Const.ChrInfoMapping[i]);
        }
        
    }
    private void randomizeItemLotParams() {
        OrderedDictionary categoryDictEnemy = new();
        OrderedDictionary categoryDictMap = new();

        IEnumerable<Param.Row> itemLotParamMap = _itemLotParam_map.Rows.Where(id => !Unk.unkItemLotParamMapWeapons.Contains(id.ID));
        IEnumerable<Param.Row> itemLotParamEnemy = _itemLotParam_enemy.Rows.Where(id => !Unk.unkItemLotParamEnemyWeapons.Contains(id.ID));
        
        foreach (Param.Row row in itemLotParamEnemy.Concat(itemLotParamMap)) {
            Param.Column[] itemIds = row.Cells.Take(Const.ItemLots).ToArray();
            Param.Column[] categories = row.Cells.Skip(Const.CategoriesStart).Take(Const.ItemLots).ToArray();
            Param.Column[] chances = row.Cells.Skip(Const.ChanceStart).Take(Const.ItemLots).ToArray();
            int totalWeight = chances.Sum(a => (ushort)a.GetValue(row));
            for (int i = 0; i < Const.ItemLots; i++) {
                int category = (int)categories[i].GetValue(row);
                if (category != Const.ItemLotWeaponCategory && category != Const.ItemLotCustomWeaponCategory) {
                    continue;
                }

                int id = (int)itemIds[i].GetValue(row);
                int sanitizedId = removeWeaponLevels(id);
                if (category == Const.ItemLotWeaponCategory) {
                    if (!_weaponDictionary.TryGetValue(sanitizedId, out EquipParamWeapon? wep)) {
                        continue;
                    }

                    if (wep.wepType is Const.StaffType or Const.SealType) {
                        continue;
                    }

                    if (id != sanitizedId) {
                        _weaponNameDictionary[id] = $"{_weaponNameDictionary[sanitizedId]} + {id - sanitizedId}";
                    }
                    ushort chance = (ushort)chances[i].GetValue(row);
                    if (chance == totalWeight) {
                        addToOrderedDict(categoryDictMap, wep.wepType, new ItemLotEntry(id, category));
                        break; // Break here because the entire item lot param is just a single entry.
                    }

                    addToOrderedDict(categoryDictEnemy, wep.wepType, new ItemLotEntry(id, category));
                }
                else { // category == Const.ItemLotCustomWeaponCategory
                    if (!_customWeaponDictionary.TryGetValue(id, out EquipParamWeapon? wep)) {
                        continue;
                    }

                    if (wep.wepType is Const.StaffType or Const.SealType) {
                        continue;
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

        dedupeAndRandomizeVectors(categoryDictMap);
        dedupeAndRandomizeVectors(categoryDictEnemy);

        Dictionary<int, ItemLotEntry> guaranteedDropReplace = getReplacementHashmap(categoryDictMap);
        Dictionary<int, ItemLotEntry> chanceDropReplace = getReplacementHashmap(categoryDictEnemy);
        logItem(">> Item Replacements - all instances of item on left will be replaced with item on right");
        logItem("> Guaranteed Weapons");
        logReplacementDictionary(guaranteedDropReplace);
        logItem("> Chance Weapons");
        logReplacementDictionary(chanceDropReplace);
        logItem("");

        foreach (Param.Row row in _itemLotParam_enemy.Rows.Concat(_itemLotParam_map.Rows)) {
            Param.Column[] itemIds = row.Cells.Take(Const.ItemLots).ToArray();
            Param.Column[] categories = row.Cells.Skip(Const.CategoriesStart).Take(Const.ItemLots).ToArray();
            for (int i = 0; i < Const.ItemLots; i++) {
                int category = (int)categories[i].GetValue(row);
                if (category != Const.ItemLotWeaponCategory && category != Const.ItemLotCustomWeaponCategory) {
                    continue;
                }

                int id = (int)itemIds[i].GetValue(row);
                if (category == Const.ItemLotWeaponCategory) {
                    if (!_weaponDictionary.TryGetValue(removeWeaponLevels(id), out _)) {
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
                else { // category == Const.ItemLotCustomWeaponCategory
                    if (!_customWeaponDictionary.TryGetValue(id, out _)) {
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
    private void randomizeShopLineupParam() {
        List<ShopLineupParam> shopLineupParamRemembranceList = new();
        foreach (Param.Row row in _shopLineupParam.Rows) {
            if ((byte)row["equipType"]!.Value.Value != Const.ShopLineupWeaponCategory || (row.ID < 101900 || row.ID > 101980)) {
                continue;
            }

            ShopLineupParam lot = new(new Param.Row(row));
            int sanitizedId = removeWeaponLevels(lot.equipId);
            if (!_weaponDictionary.TryGetValue(sanitizedId, out _)) {
                continue;
            }

            if (lot.equipId != sanitizedId) {
                _weaponNameDictionary[lot.equipId] = $"{_weaponNameDictionary[sanitizedId]} +{lot.equipId - sanitizedId}";
            }
            shopLineupParamRemembranceList.Add(lot);
        }

        List<Param.Row> staves = _weaponTypeDictionary[Const.StaffType];
        List<Param.Row> seals = _weaponTypeDictionary[Const.SealType];
        List<int> shopLineupParamList = _weaponDictionary.Keys.Select(removeWeaponMetadata).Distinct()
            .Where(i => shopLineupParamRemembranceList.All(s => s.equipId != i))
            .Where(id => staves.All(s => s.ID != id) && seals.All(s => s.ID != id))
            .ToList();
        shopLineupParamList.Shuffle(_random);
        shopLineupParamRemembranceList.Shuffle(_random);

        logItem(">> Shop Replacements - Random item selected from pool of all weapons (not including infused weapons). Remembrances are randomized amongst each-other.");

        foreach (Param.Row row in _shopLineupParam.Rows) {
            logShopId(row.ID);
            if ((byte)row["equipType"]!.Value.Value != Const.ShopLineupWeaponCategory || row.ID > 101980) {
                continue;
            }

            ShopLineupParam lot = new(row);
            if (!_weaponDictionary.TryGetValue(removeWeaponLevels(lot.equipId), out EquipParamWeapon? wep)) {
                continue;
            }
            if (wep.wepType is Const.StaffType or Const.SealType) {
                continue;
            }

            replaceShopLineupParam(lot, shopLineupParamList, shopLineupParamRemembranceList);
        }
    }
    private void randomizeShopLineupParamMagic() {
        OrderedDictionary magicCategoryDictMap = new();
        List<ShopLineupParam> shopLineupParamRemembranceList = new();
        List<ShopLineupParam> shopLineupParamDragonList = new();
        foreach (Param.Row row in _shopLineupParam.Rows) {
            if ((byte)row["equipType"]!.Value.Value != Const.ShopLineupGoodsCategory || row.ID > 101980) {
                continue;
            }

            ShopLineupParam lot = new(new Param.Row(row));
            if (!_magicDictionary.TryGetValue(lot.equipId, out Magic? magic)) {
                continue;
            }
            if (row.ID < 101950) {
                if (lot.mtrlId == -1) {
                    addToOrderedDict(magicCategoryDictMap, magic.ezStateBehaviorType, lot.equipId);
                    continue;
                }
                shopLineupParamRemembranceList.Add(lot);
            }
            else { // Dragon Communion Shop 101950 - 101980 
                shopLineupParamDragonList.Add(lot);
            }
        }

        foreach (Param.Row row in _itemLotParam_enemy.Rows.Concat(_itemLotParam_map.Rows)) {
            Param.Column[] itemIds = row.Cells.Take(Const.ItemLots).ToArray();
            Param.Column[] categories = row.Cells.Skip(Const.CategoriesStart).Take(Const.ItemLots).ToArray();
            Param.Column[] chances = row.Cells.Skip(Const.ChanceStart).Take(Const.ItemLots).ToArray();
            int totalWeight = chances.Sum(a => (ushort)a.GetValue(row));
            for (int i = 0; i < Const.ItemLots; i++) {
                int category = (int)categories[i].GetValue(row);
                if (category != Const.ItemLotGoodsCategory) {
                    continue;
                }

                int id = (int)itemIds[i].GetValue(row);
                if (!_magicDictionary.TryGetValue(id, out Magic? magic)) {
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
        shopLineupParamRemembranceList.Shuffle(_random);
        shopLineupParamDragonList.Shuffle(_random);
        logItem("\n>> All Magic Replacement.");
        logReplacementDictionaryMagic(magicShopReplacement);

        logItem("\n>> Shop Magic Replacement.");
        foreach (Param.Row row in _shopLineupParam.Rows) {
            logShopIdMagic(row.ID);
            if ((byte)row["equipType"]!.Value.Value != Const.ShopLineupGoodsCategory || row.ID > 101980) {
                continue;
            }

            ShopLineupParam lot = new(row);
            if (!_magicDictionary.TryGetValue(lot.equipId, out _)) {
                continue;
            }
            if (row.ID < 101950) {
                replaceShopLineupParamMagic(lot, magicShopReplacement, shopLineupParamRemembranceList);
            }
            else {
                ShopLineupParam newDragonIncant = getNewId(lot.equipId, shopLineupParamDragonList);
                logItem($"{_goodsFmg[0][lot.equipId]} -> {_goodsFmg[0][newDragonIncant.equipId]}");
                copyShopLineupParam(lot, newDragonIncant);
            }
        }

        foreach (Param.Row row in _itemLotParam_enemy.Rows.Concat(_itemLotParam_map.Rows)) {
            Param.Column[] itemIds = row.Cells.Take(Const.ItemLots).ToArray();
            Param.Column[] categories = row.Cells.Skip(Const.CategoriesStart).Take(Const.ItemLots).ToArray();
            for (int i = 0; i < Const.ItemLots; i++) {
                int category = (int)categories[i].GetValue(row);
                if (category != Const.ItemLotGoodsCategory) {
                    continue;
                }

                int id = (int)itemIds[i].GetValue(row);
                if (!_magicDictionary.TryGetValue(id, out Magic _)) {
                    continue;
                }

                if (!magicShopReplacement.TryGetValue(id, out int entry)) {
                    continue;
                }
                itemIds[i].SetValue(row, entry);
            }
        }
    }
    private void patchAtkParam() {
        Param.Row swarmOfFlies1 = _atkParam_Pc[72100]?? throw new InvalidOperationException("Entry 72100 not found in AtkParam_Pc");
        Param.Row swarmOfFlies2 = _atkParam_Pc[72101]?? throw new InvalidOperationException("Entry 72101 not found in AtkParam_Pc");

        AtkParam swarmAtkParam1 = new(swarmOfFlies1);
        AtkParam swarmAtkParam2 = new(swarmOfFlies2);
        patchSpEffectAtkPowerCorrectRate(swarmAtkParam1);
        patchSpEffectAtkPowerCorrectRate(swarmAtkParam2);
    }
}
