﻿using ERBingoRandomizer.Params;
using ERBingoRandomizer.Utility;
using FSParam;
using SoulsFormats;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace ERBingoRandomizer.Randomizer;

public partial class BingoRandomizer {
    private List<string> _randomizerLog;
    private void logItem(string item) {
        _randomizerLog.Add(item);
    }
    private void writeLog() {
        Directory.CreateDirectory(Config.SpoilerPath);
        File.WriteAllLines($"{Config.SpoilerPath}/spoiler-{_seed}.log", _randomizerLog);
    }
    private void guaranteeSpellcasters(int rowId, CharaInitParam chr, IReadOnlyList<int> spells) {
        switch (rowId) {
            case 3008:
                guaranteePrisonerHasSpells(chr, spells);
                break;
            case 3006:
                guaranteeConfessorHasIncantation(chr, spells);
                break;
        }
    }
    private void guaranteePrisonerHasSpells(CharaInitParam chr, IReadOnlyList<int> spells) {
        if (hasSpellOfType(chr, Const.SorceryType)) {
            return;
        }
        // Get a new random chr until it has the required stats.
        while (chr.baseMag < Config.MinInt) {
            randomizeLevels(chr);
        }

        chr.equipSpell01 = -1;
        chr.equipSpell02 = -1;
        randomizeSorceries(chr, spells);
    }
    private void guaranteeConfessorHasIncantation(CharaInitParam chr, IReadOnlyList<int> spells) {
        if (hasSpellOfType(chr, Const.IncantationType)) {
            return;
        }
        // Get a new random chr until it has the required stats.
        while (chr.baseFai < Config.MinFai) {
            randomizeLevels(chr);
        }

        chr.equipSpell01 = -1;
        chr.equipSpell02 = -1;
        randomizeIncantations(chr, spells);
    }
    private void randomizeCharaInitEntry(CharaInitParam chr, IReadOnlyList<int> weapons) {
        chr.wepleft = getRandomWeapon(chr.wepleft, weapons);
        chr.wepRight = getRandomWeapon(chr.wepRight, weapons);
        chr.subWepLeft = -1;
        chr.subWepRight = -1;
        chr.subWepLeft3 = -1;
        chr.subWepRight3 = -1;

        chr.equipHelm = chanceGetRandomArmor(chr.equipHelm, Const.HelmType);
        chr.equipArmer = chanceGetRandomArmor(chr.equipArmer, Const.BodyType);
        chr.equipGaunt = chanceGetRandomArmor(chr.equipGaunt, Const.ArmType);
        chr.equipLeg = chanceGetRandomArmor(chr.equipLeg, Const.LegType);

        randomizeLevels(chr);

        chr.equipArrow = Const.NoItem;
        chr.arrowNum = ushort.MaxValue;
        if (hasWeaponOfType(chr, Const.BowType, Const.LightBowType)) {
            giveArrows(chr);
        }
        chr.equipSubArrow = Const.NoItem;
        chr.subArrowNum = ushort.MaxValue;
        if (hasWeaponOfType(chr, Const.GreatbowType)) {
            giveGreatArrows(chr);
        }
        chr.equipBolt = Const.NoItem;
        chr.boltNum = ushort.MaxValue;
        if (hasWeaponOfType(chr, Const.CrossbowType)) {
            giveBolts(chr);
        }
        chr.equipSubBolt = Const.NoItem;
        chr.subBoltNum = ushort.MaxValue;
        if (hasWeaponOfType(chr, Const.BallistaType)) {
            giveBallistaBolts(chr);
        }

        chr.equipSpell01 = -1;
        chr.equipSpell02 = -1;
    }
    private Dictionary<int, ItemLotEntry> getReplacementHashmap(IOrderedDictionary orderedDictionary) {
        Dictionary<int, ItemLotEntry> dict = new();

        List<ItemLotEntry> bows = (List<ItemLotEntry>?)orderedDictionary[(object)Const.BowType] ?? new List<ItemLotEntry>();
        List<ItemLotEntry> lightbows = (List<ItemLotEntry>?)orderedDictionary[(object)Const.LightBowType] ?? new List<ItemLotEntry>();
        List<ItemLotEntry> greatbows = (List<ItemLotEntry>?)orderedDictionary[(object)Const.GreatbowType] ?? new List<ItemLotEntry>();
        List<ItemLotEntry> crossbows = (List<ItemLotEntry>?)orderedDictionary[(object)Const.CrossbowType] ?? new List<ItemLotEntry>();
        List<ItemLotEntry> ballista = (List<ItemLotEntry>?)orderedDictionary[(object)Const.BallistaType] ?? new List<ItemLotEntry>();

        bows.AddRange(lightbows);
        bows.AddRange(greatbows);
        bows.AddRange(crossbows);
        bows.AddRange(ballista);
        orderedDictionary[(object)Const.BowType] = bows;
        orderedDictionary.Remove(Const.LightBowType);
        orderedDictionary.Remove(Const.GreatbowType);
        orderedDictionary.Remove(Const.CrossbowType);
        orderedDictionary.Remove(Const.BallistaType);

        for (int i = 0; i < orderedDictionary.Count; i++) {
            List<ItemLotEntry> value = (List<ItemLotEntry>)orderedDictionary[i]!;
            List<ItemLotEntry> itemLotEntries = new(value);
            itemLotEntries.Shuffle(_random);
            foreach (ItemLotEntry entry in itemLotEntries) {
                dict.Add(entry.Id, getNewId(entry.Id, value));
            }
        }

        return dict;
    }
    private Dictionary<int, int> getShopReplacementHashmap(IOrderedDictionary orderedDictionary) {
        Dictionary<int, int> dict = new();
        for (int i = 0; i < orderedDictionary.Count; i++) {
            List<int> value = (List<int>)orderedDictionary[i]!;
            List<int> itemLotEntries = new(value);
            itemLotEntries.Shuffle(_random);
            foreach (int entry in itemLotEntries) {
                dict.Add(entry, getNewId(entry, value));
            }
        }

        return dict;
    }
    private void dedupeAndRandomizeVectors(IOrderedDictionary orderedDictionary) {
        for (int i = 0; i < orderedDictionary.Count; i++) {
            List<ItemLotEntry> value = (List<ItemLotEntry>)orderedDictionary[i]!;
            List<ItemLotEntry> distinct = value.Distinct().ToList();
            distinct.Shuffle(_random);
            orderedDictionary[i] = distinct;
        }
    }
    private void dedupeAndRandomizeShopVectors(IOrderedDictionary orderedDictionary) {
        for (int i = 0; i < orderedDictionary.Count; i++) {
            List<int> value = (List<int>)orderedDictionary[i]!;
            List<int> distinct = value.Distinct().ToList();
            distinct.Shuffle(_random);
            orderedDictionary[i] = distinct;
        }
    }
    private void replaceShopLineupParam(ShopLineupParam lot, IList<int> shopLineupParamDictionary, IList<ShopLineupParam> shopLineupParamRemembranceList) {
        if (lot.mtrlId == -1) {
            int newId = getNewId(lot.equipId, shopLineupParamDictionary);
            logItem($"{_weaponNameDictionary[lot.equipId]} -> {_weaponNameDictionary[newId]}");
            lot.equipId = newId;
            return;
        }
        ShopLineupParam newRemembrance = getNewId(lot.equipId, shopLineupParamRemembranceList);
        logItem($"{_weaponNameDictionary[lot.equipId]} -> {_weaponNameDictionary[newRemembrance.equipId]}");
        copyShopLineupParam(lot, newRemembrance);
    }
    private void replaceShopLineupParamMagic(ShopLineupParam lot, IReadOnlyDictionary<int, int> shopLineupParamDictionary, IList<ShopLineupParam> shopLineupParamRemembranceList) {
        if (lot.mtrlId == -1) {
            int newItem = shopLineupParamDictionary[lot.equipId];
            logItem($"{_goodsFmg[0][lot.equipId]} -> {_goodsFmg[0][newItem]}");
            lot.equipId = newItem;
            return;
        }
        ShopLineupParam newRemembrance = getNewId(lot.equipId, shopLineupParamRemembranceList);
        logItem($"{_goodsFmg[0][lot.equipId]} -> {_goodsFmg[0][newRemembrance.equipId]}");
        copyShopLineupParam(lot, newRemembrance);
    }
    private void addDescriptionString(CharaInitParam chr, int id) {
        for (int i = 0; i < Const.ERLanguageNames.Length; i++)
        {
            List<string> str = new()
            {
                $"{_weaponFmg[i][chr.wepleft]}{getRequiredLevelsWeapon(chr, chr.wepleft)}",
                $"{_weaponFmg[i][chr.wepRight]}{getRequiredLevelsWeapon(chr, chr.wepRight)}",
            };
            if (chr.subWepLeft != -1)
            {
                str.Add($"{_weaponFmg[i][chr.subWepLeft]}{getRequiredLevelsWeapon(chr, chr.subWepLeft)}");
            }

            if (chr.subWepRight != -1)
            {
                str.Add($"{_weaponFmg[i][chr.subWepRight]}{getRequiredLevelsWeapon(chr, chr.subWepRight)}");
            }

            if (chr.subWepLeft3 != -1)
            {
                str.Add($"{_weaponFmg[i][chr.subWepLeft3]}{getRequiredLevelsWeapon(chr, chr.subWepLeft3)}");
            }

            if (chr.subWepRight3 != -1)
            {
                str.Add($"{_weaponFmg[i][chr.subWepRight3]}{getRequiredLevelsWeapon(chr, chr.subWepRight3)}");
            }

            if (chr.equipArrow != -1)
            {
                str.Add($"{_weaponFmg[i][chr.equipArrow]}[{chr.arrowNum}]");
            }

            if (chr.equipSubArrow != -1)
            {
                str.Add($"{_weaponFmg[i][chr.equipSubArrow]}[{chr.subArrowNum}]");
            }

            if (chr.equipBolt != -1)
            {
                str.Add($"{_weaponFmg[i][chr.equipBolt]}[{chr.boltNum}]");
            }

            if (chr.equipSubBolt != -1)
            {
                str.Add($"{_weaponFmg[i][chr.equipSubBolt]}[{chr.subBoltNum}]");
            }

            if (chr.equipSpell01 != -1)
            {
                str.Add($"{_goodsFmg[i][chr.equipSpell01]}");
            }

            if (chr.equipSpell02 != -1)
            {
                str.Add($"{_goodsFmg[i][chr.equipSpell02]}");
            }

            _lineHelpFmg[i][id] = string.Join(", ", str);
        }
    }
    private void writeFiles() {
        if (Directory.Exists(Const.BingoPath)) {
            Directory.Delete(Const.BingoPath, true);
        }
        Directory.CreateDirectory(Path.GetDirectoryName($"{Const.BingoPath}/{Const.RegulationName}") ?? throw new InvalidOperationException());
        setBndFile(_regulationBnd, Const.CharaInitParamName, _charaInitParam.Write());
        setBndFile(_regulationBnd, Const.ItemLotParam_mapName, _itemLotParam_map.Write());
        setBndFile(_regulationBnd, Const.ItemLotParam_enemyName, _itemLotParam_enemy.Write());
        setBndFile(_regulationBnd, Const.ShopLineupParamName, _shopLineupParam.Write());
        setBndFile(_regulationBnd, Const.EquipParamWeaponName, _equipParamWeapon.Write());
        setBndFile(_regulationBnd, Const.AtkParamPcName, _atkParam_Pc.Write());
        SFUtil.EncryptERRegulation($"{Const.BingoPath}/{Const.RegulationName}", _regulationBnd);
        for (int i = 0; i < Const.ERLanguageNames.Length; i++)
        {
            string menuMsgBndPath = $"/msg/{Const.ERLanguageNames[i]}/menu.msgbnd.dcx";
            Directory.CreateDirectory(Path.GetDirectoryName($"{Const.BingoPath}{menuMsgBndPath}") ?? throw new InvalidOperationException());
            setBndFile(_menuMsgBND[i], Const.GR_LineHelpName, _lineHelpFmg[i].Write());
            File.WriteAllBytes($"{Const.BingoPath}{menuMsgBndPath}", _menuMsgBND[i].Write());
        }
    }
    private void logReplacementDictionary(Dictionary<int, ItemLotEntry> dict) {
        foreach (KeyValuePair<int, ItemLotEntry> pair in dict) {
            logItem($"{_weaponNameDictionary[pair.Key]} -> {_weaponNameDictionary[pair.Value.Id]}");
        }
    }
    private void logReplacementDictionaryMagic(Dictionary<int, int> dict) {
        foreach (KeyValuePair<int, int> pair in dict) {
            logItem($"{_goodsFmg[0][pair.Key]} -> {_goodsFmg[0][pair.Value]}");
        }
    }
    private void logCharaInitEntry(CharaInitParam chr, int i) {
        logItem($"\n> {_menuTextFmg[i]}");
        logItem("> Weapons");
        if (chr.wepleft != -1) {
            logItem($"Left: {_weaponFmg[0][chr.wepleft]}{getRequiredLevelsWeapon(chr, chr.wepleft)}");
        }
        if (chr.wepRight != -1) {
            logItem($"Right: {_weaponFmg[0][chr.wepRight]}{getRequiredLevelsWeapon(chr, chr.wepRight)}");
        }
        if (chr.subWepLeft != -1) {
            logItem($"Left 2: {_weaponFmg[0][chr.subWepLeft]}{getRequiredLevelsWeapon(chr, chr.subWepLeft)}");
        }
        if (chr.subWepRight != -1) {
            logItem($"Right 2: {_weaponFmg[0][chr.subWepRight]}{getRequiredLevelsWeapon(chr, chr.subWepRight)}");
        }
        if (chr.subWepLeft3 != -1) {
            logItem($"Left 3: {_weaponFmg[0][chr.subWepLeft3]}{getRequiredLevelsWeapon(chr, chr.subWepLeft3)}");
        }
        if (chr.subWepRight3 != -1) {
            logItem($"Right 3: {_weaponFmg[0][chr.subWepRight3]}{getRequiredLevelsWeapon(chr, chr.subWepRight3)}");
        }

        logItem("\n> Armor");
        logItem($"Helm: {_protectorFmg[chr.equipHelm]}");
        logItem($"Body: {_protectorFmg[chr.equipArmer]}");
        logItem($"Arms: {_protectorFmg[chr.equipGaunt]}");
        logItem($"Legs: {_protectorFmg[chr.equipLeg]}");

        logItem("\n> Levels");
        logItem($"Vigor: {chr.baseVit}");
        logItem($"Attunement: {chr.baseWil}");
        logItem($"Endurance: {chr.baseEnd}");
        logItem($"Strength: {chr.baseStr}");
        logItem($"Dexterity: {chr.baseDex}");
        logItem($"Intelligence: {chr.baseMag}");
        logItem($"Faith: {chr.baseFai}");
        logItem($"Arcane: {chr.baseLuc}");

        if (chr.equipArrow != -1 || chr.equipSubArrow != -1 || chr.equipBolt != -1 || chr.equipSubBolt != -1) {
            logItem("\n> Ammo");
            if (chr.equipArrow != -1) {
                logItem($"{_weaponFmg[0][chr.equipArrow]}[{chr.arrowNum}]");
            }
            if (chr.equipSubArrow != -1) {
                logItem($"{_weaponFmg[0][chr.equipSubArrow]}[{chr.subArrowNum}]");
            }
            if (chr.equipBolt != -1) {
                logItem($"{_weaponFmg[0][chr.equipBolt]}[{chr.boltNum}]");
            }
            if (chr.equipSubBolt != -1) {
                logItem($"{_weaponFmg[0][chr.equipSubBolt]}[{chr.subBoltNum}]");
            }
        }

        if (chr.equipSpell01 != -1 || chr.equipSpell02 != -1) {
            logItem("\n> Spells");
            if (chr.equipSpell01 != -1) {
                logItem($"{_goodsFmg[0][chr.equipSpell01]}{getRequiredLevelsSpell(chr, chr.equipSpell01)}");
            }
            if (chr.equipSpell02 != -1) {
                logItem($"{_goodsFmg[0][chr.equipSpell02]}{getRequiredLevelsSpell(chr, chr.equipSpell02)}");
            }
        }

        logItem("");
    }
    private string getRequiredLevelsWeapon(CharaInitParam chr, int id) {
        EquipParamWeapon wep = _weaponDictionary[id];
        int reqLevels = 0;
        if (wep.properStrength > chr.baseStr) {
            reqLevels += wep.properStrength - chr.baseStr;
        }
        if (wep.properAgility > chr.baseDex) {
            reqLevels += wep.properAgility - chr.baseDex;
        }
        if (wep.properMagic > chr.baseMag) {
            reqLevels += wep.properMagic - chr.baseMag;
        }
        if (wep.properFaith > chr.baseFai) {
            reqLevels += wep.properFaith - chr.baseFai;
        }
        if (wep.properLuck > chr.baseLuc) {
            reqLevels += wep.properLuck - chr.baseLuc;
        }

        return reqLevels > 0 ? $" (-{reqLevels})" : "";

    }
    private string getRequiredLevelsSpell(CharaInitParam chr, int id) {
        Magic spell = _magicDictionary[id];
        int reqLevels = 0;
        if (spell.requirementIntellect > chr.baseMag) {
            reqLevels += spell.requirementIntellect - chr.baseMag;
        }
        if (spell.requirementFaith > chr.baseFai) {
            reqLevels += spell.requirementFaith - chr.baseFai;
        }
        if (spell.requirementLuck > chr.baseLuc) {
            reqLevels += spell.requirementLuck - chr.baseLuc;
        }

        return reqLevels > 0 ? $" (-{reqLevels})" : "";

    }
    private void logShopId(int rowId) {
        switch (rowId) {
            case 100000:
                logItem("\n> Gatekeeper Gostoc");
                break;
            case 100100:
                logItem("\n> Patches");
                break;
            case 100325:
                logItem("\n> Pidia Carian Servant");
                break;
            case 100500:
                logItem("\n> Merchant Kale");
                break;
            case 100525:
                logItem("\n> Merchant - North Limgrave");
                break;
            case 100550:
                logItem("\n> Merchant - East Limgrave");
                break;
            case 100575:
                logItem("\n> Merchant - Coastal Cave");
                break;
            case 100600:
                logItem("\n> Merchant - East Weeping Peninsula");
                break;
            case 100625:
                logItem("\n> Merchant - Liurnia of the Lakes");
                break;
            case 100650:
                logItem("\n> Isolated Merchant - Weeping Peninsula");
                break;
            case 100700:
                logItem("\n> Merchant - North Liurnia");
                break;
            case 100725:
                logItem("\n> Hermit Merchant - Leyndell");
                break;
            case 100750:
                logItem("\n> Merchant - Altus Plateau");
                break;
            case 100875:
                logItem("\n> Isolated Merchant - Dragonbarrow");
                break;
            case 100925:
                logItem("\n> Merchant - Siofra River");
                break;
            case 101800:
                logItem("\n> Twin Maiden Husks");
                break;
            case 101900:
                logItem("\n> Remembrances");
                break;
        }
    }
    private void logShopIdMagic(int rowId) {
        switch (rowId) {
            case 100050:
                logItem("\n> Sorceress Sellen");
                break;
            case 100056:
                logItem("\n> Sorceress Sellen - Quest");
                break;
            case 100057:
                logItem("\n> Sorceress Sellen - Conspectus Scroll");
                break;
            case 100059:
                logItem("\n> Sorceress Sellen -  Academy Scroll");
                break;
            case 100061:
                logItem("\n> Sorceress Sellen");
                break;
            case 100126:
                logItem("\n> D Hunter of The Dead");
                break;
            case 100175:
                logItem("\n> Gowry");
                break;
            case 100250:
                logItem("\n> Preceptor Seluvis");
                break;
            case 100300:
                logItem("\n> Preceptor Seluvis - Ranni Quest");
                break;
            case 100310:
                logItem("\n> Preceptor Seluvis - Dung Eater Quest");
                break;
            case 100350:
                logItem("\n> Brother Corhyn");
                break;
            case 100358:
                logItem("\n> Brother Corhyn - Altus Plateau");
                break;
            case 100360:
                logItem("\n> Brother Corhyn - Goldmask");
                break;
            case 100361:
                logItem("\n> Brother Corhyn - Erdtree Sanctuary");
                break;
            case 100362:
                logItem("\n> Brother Corhyn - Fire Monks' Prayerbook");
                break;
            case 100364:
                logItem("\n> Brother Corhyn - Giant's Prayerbook");
                break;
            case 100368:
                logItem("\n> Brother Corhyn - Two Fingers' Prayerbook");
                break;
            case 100370:
                logItem("\n> Brother Corhyn - Assassin's Prayerbook");
                break;
            case 100372:
                logItem("\n> Brother Corhyn - Golden Order Principia");
                break;
            case 100374:
                logItem("\n> Brother Corhyn - Dragon Cult Prayerbook");
                break;
            case 100377:
                logItem("\n> Brother Corhyn - Ancient Dragon Prayerbook");
                break;
            case 100400:
                logItem("\n> Miriel");
                break;
            case 100402:
                logItem("\n> Miriel - Conspectus Scroll");
                break;
            case 100404:
                logItem("\n> Miriel - Academy Scroll");
                break;
            case 100406:
                logItem("\n> Miriel");
                break;
            case 100426:
                logItem("\n> Miriel - Fire Monks' Prayerbook");
                break;
            case 100429:
                logItem("\n> Miriel - Giant's Prayerbook");
                break;
            case 100433:
                logItem("\n> Miriel - Two Fingers' Prayerbook");
                break;
            case 100435:
                logItem("\n> Miriel - Assassin's Prayerbook");
                break;
            case 100437:
                logItem("\n> Miriel - Golden Order Principia");
                break;
            case 100439:
                logItem("\n> Miriel - Dragon Cult Prayerbook");
                break;
            case 100442:
                logItem("\n> Miriel - Ancient Dragon Prayerbook");
                break;
            case 101905:
                logItem("\n> Remembrance");
                break;
            case 101950:
                logItem("\n> Dragon Communion");
                break;
        }
    }
    private void calculateLevels() {
        for (int i = 0; i < 10; i++) {
            Param.Row? row = _charaInitParam[i + 3000];
            if (row == null) {
                continue;
            }
            CharaInitParam chr = new(row);

            Debug.WriteLine($"{_menuTextFmg[i + 288100]} {chr.soulLv} {addLevels(chr)}");
        }
    }
    private static T getNewId<T>(int oldId, IList<T> vec) where T : IEquatable<int> {
        if (vec.All(i => i.Equals(oldId))) {
            Debug.WriteLine($"No New Ids for {oldId}");
            return vec.Pop();
        }

        T newId = vec.Pop();
        while (newId.Equals(oldId)) {
            vec.Insert(0, newId);
            newId = vec.Pop();
        }

        return newId;
    }
    // ReSharper disable once SuggestBaseTypeForParameter
    private static void addToOrderedDict<T>(IOrderedDictionary orderedDict, object key, T type) {
        List<T>? ids = (List<T>?)orderedDict[key];
        if (ids != null) {
            ids.Add(type);
        }
        else {
            ids = new List<T> {
                type,
            };
            orderedDict.Add(key, ids);
        }
    }
    private static bool chrCanUseWeapon(EquipParamWeapon wep, CharaInitParam chr) {
        return wep.properStrength <= chr.baseStr
            && wep.properAgility <= chr.baseDex
            && wep.properMagic <= chr.baseMag
            && wep.properFaith <= chr.baseFai
            && wep.properLuck <= chr.baseLuc;
    }
    private static bool chrCanUseSpell(Magic spell, CharaInitParam chr) {
        return spell.requirementIntellect <= chr.baseMag
            && spell.requirementFaith <= chr.baseFai
            && spell.requirementLuck <= chr.baseLuc;
    }
    private static int getSeedFromHashData(IEnumerable<byte> hashData) {
        IEnumerable<byte[]> chunks = hashData.Chunk(4);
        return chunks.Aggregate(0, (current, chunk) => current ^ BitConverter.ToInt32(chunk));
    }
    private static void setBndFile(IBinder binder, string fileName, byte[] bytes) {
        BinderFile file = binder.Files.First(file => file.Name.EndsWith(fileName)) ?? throw new BinderFileNotFoundException(fileName);
        ;
        file.Bytes = bytes;
    }
    private static void patchSpEffectAtkPowerCorrectRate(AtkParam atkParam) {
        atkParam.spEffectAtkPowerCorrectRate_byPoint = 100;
        atkParam.spEffectAtkPowerCorrectRate_byRate = 100;
        atkParam.spEffectAtkPowerCorrectRate_byDmg = 100;
    }
    private static void copyShopLineupParam(ShopLineupParam lot, ShopLineupParam shopLineupParam) {
        lot.equipId = shopLineupParam.equipId;
        lot.costType = shopLineupParam.costType;
        lot.sellQuantity = shopLineupParam.sellQuantity;
        lot.setNum = shopLineupParam.setNum;
        lot.value = shopLineupParam.value;
        lot.value_Add = shopLineupParam.value_Add;
        lot.value_Magnification = shopLineupParam.value_Magnification;
        lot.iconId = shopLineupParam.iconId;
        lot.nameMsgId = shopLineupParam.nameMsgId;
        lot.menuIconId = shopLineupParam.menuIconId;
        lot.menuTitleMsgId = shopLineupParam.menuTitleMsgId;
    }
    private static int removeWeaponMetadata(int id) {
        return id / 10000 * 10000;
    }
    private static int removeWeaponLevels(int id) {
        return id / 100 * 100;
    }
    private static int addLevels(CharaInitParam chr) {
        return chr.baseVit
            + chr.baseWil
            + chr.baseEnd
            + chr.baseStr
            + chr.baseDex
            + chr.baseMag
            + chr.baseFai
            + chr.baseLuc;
    }
}
