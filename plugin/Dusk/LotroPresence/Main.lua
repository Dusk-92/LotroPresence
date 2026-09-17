import "Turbine";
import "Turbine.Gameplay";
import "Turbine.UI";

local VERSION = "0.1.0";
local DATA_KEY = "LotroPresence";
local CHECK_INTERVAL = 2;
local HEARTBEAT_INTERVAL = 10;

local player = Turbine.Gameplay.LocalPlayer:GetInstance();
local timer = Turbine.UI.Control();
local lastCheck = 0;
local lastHeartbeat = 0;
local lastFingerprint = nil;

local function addEnumName(map, enumTable, key, label)
    if enumTable ~= nil and enumTable[key] ~= nil then
        map[enumTable[key]] = label;
    end
end

local classNames = {};
addEnumName(classNames, Turbine.Gameplay.Class, "Beorning", "Béornide");
addEnumName(classNames, Turbine.Gameplay.Class, "Brawler", "Bagarreur");
addEnumName(classNames, Turbine.Gameplay.Class, "Burglar", "Cambrioleur");
addEnumName(classNames, Turbine.Gameplay.Class, "Captain", "Capitaine");
addEnumName(classNames, Turbine.Gameplay.Class, "Champion", "Champion");
addEnumName(classNames, Turbine.Gameplay.Class, "Guardian", "Gardien");
addEnumName(classNames, Turbine.Gameplay.Class, "Hunter", "Chasseur");
addEnumName(classNames, Turbine.Gameplay.Class, "LoreMaster", "Maître du savoir");
addEnumName(classNames, Turbine.Gameplay.Class, "Mariner", "Marin");
addEnumName(classNames, Turbine.Gameplay.Class, "Minstrel", "Ménestrel");
addEnumName(classNames, Turbine.Gameplay.Class, "RuneKeeper", "Gardien des runes");
addEnumName(classNames, Turbine.Gameplay.Class, "Warden", "Sentinelle");

local raceNames = {};
addEnumName(raceNames, Turbine.Gameplay.Race, "Beorning", "Béornide");
addEnumName(raceNames, Turbine.Gameplay.Race, "Dwarf", "Nain");
addEnumName(raceNames, Turbine.Gameplay.Race, "Elf", "Elfe");
addEnumName(raceNames, Turbine.Gameplay.Race, "HighElf", "Haut-Elfe");
addEnumName(raceNames, Turbine.Gameplay.Race, "Hobbit", "Hobbit");
addEnumName(raceNames, Turbine.Gameplay.Race, "Man", "Homme");
addEnumName(raceNames, Turbine.Gameplay.Race, "StoutAxe", "Hache-forte");

local function safeCall(fn, fallback)
    local ok, value = pcall(fn);
    if ok and value ~= nil then
        return value;
    end
    return fallback;
end

local function getPartySize()
    return safeCall(function()
        local party = player:GetParty();
        if party == nil then
            return 1;
        end
        return party:GetMemberCount();
    end, 1);
end

local function isInBearForm()
    return safeCall(function()
        local attributes = player:GetAttributes();
        if attributes ~= nil and attributes.IsInBearForm ~= nil then
            return attributes:IsInBearForm();
        end
        return false;
    end, false);
end

local function buildSnapshot(active)
    local classId = safeCall(function() return player:GetClass(); end, 0);
    local raceId = safeCall(function() return player:GetRace(); end, 0);

    return {
        schemaVersion = 1,
        pluginVersion = VERSION,
        active = active,
        heartbeat = safeCall(function() return Turbine.Engine.GetLocalTime(); end, 0),
        character = safeCall(function() return player:GetName(); end, ""),
        level = safeCall(function() return player:GetLevel(); end, 0),
        classId = classId,
        className = classNames[classId] or "",
        raceId = raceId,
        raceName = raceNames[raceId] or "",
        inCombat = safeCall(function() return player:IsInCombat(); end, false),
        partySize = getPartySize(),
        bearForm = isInBearForm()
    };
end

local function fingerprint(data)
    return table.concat({
        tostring(data.character),
        tostring(data.level),
        tostring(data.classId),
        tostring(data.raceId),
        tostring(data.inCombat),
        tostring(data.partySize),
        tostring(data.bearForm)
    }, "|");
end

local function saveSnapshot(forceHeartbeat, active)
    local data = buildSnapshot(active);
    local currentFingerprint = fingerprint(data);
    local now = Turbine.Engine.GetGameTime();

    if forceHeartbeat or currentFingerprint ~= lastFingerprint or (now - lastHeartbeat) >= HEARTBEAT_INTERVAL then
        Turbine.PluginData.Save(Turbine.DataScope.Character, DATA_KEY, data);
        lastFingerprint = currentFingerprint;
        lastHeartbeat = now;
    end
end

timer:SetWantsUpdates(true);
timer.Update = function(sender, args)
    local now = Turbine.Engine.GetGameTime();
    if (now - lastCheck) < CHECK_INTERVAL then
        return;
    end

    lastCheck = now;
    saveSnapshot(false, true);
end

saveSnapshot(true, true);

if plugin ~= nil then
    plugin.Unload = function()
        timer:SetWantsUpdates(false);
        saveSnapshot(true, false);
    end;
end

Turbine.Shell.WriteLine("LotroPresence " .. VERSION .. " chargé.");
