import "Turbine";
import "Turbine.Gameplay";
import "Turbine.UI";

local VERSION = "0.4.0";
local DATA_KEY = "LotroPresence";
local CHECK_INTERVAL = 2;
local HEARTBEAT_INTERVAL = 20;

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

local function addEnumNameByPattern(map, enumTable, requiredParts, label)
    if enumTable == nil then
        return;
    end

    pcall(function()
        for key, value in pairs(enumTable) do
            local normalized = string.lower(tostring(key));
            local matches = true;

            for index = 1, table.getn(requiredParts) do
                if string.find(normalized, requiredParts[index], 1, true) == nil then
                    matches = false;
                    break;
                end
            end

            if matches and type(value) == "number" then
                map[value] = label;
            end
        end
    end);
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

-- Les documentations Lua publiques ne sont pas toujours à jour avec les races récentes.
-- On couvre les noms d'énumération plausibles puis on cherche aussi dynamiquement
-- toute entrée contenant à la fois "river" et "hobbit".
addEnumName(raceNames, Turbine.Gameplay.Race, "RiverHobbit", "Hobbit des Rivières");
addEnumName(raceNames, Turbine.Gameplay.Race, "RiverHobbitRace", "Hobbit des Rivières");
addEnumNameByPattern(raceNames, Turbine.Gameplay.Race, { "river", "hobbit" }, "Hobbit des Rivières");

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

        local memberCount = party:GetMemberCount();
        if memberCount == nil or memberCount < 0 then
            memberCount = 0;
        end

        -- Selon la version/API, la liste des PartyMember peut inclure ou non
        -- le joueur local. On le détecte au lieu de supposer un comportement.
        local localName = player:GetName();
        local includesLocalPlayer = false;

        for index = 1, memberCount do
            local member = party:GetMember(index);
            local memberName = safeCall(function()
                if member == nil then return ""; end
                return member:GetName();
            end, "");
            if memberName == localName then
                includesLocalPlayer = true;
                break;
            end
        end

        local total = memberCount;
        if not includesLocalPlayer then
            total = total + 1;
        end

        if total < 1 then
            return 1;
        end

        return total;
    end, 1);
end

local function buildSnapshot(active)
    local classId = safeCall(function() return player:GetClass(); end, 0);
    local raceId = safeCall(function() return player:GetRace(); end, 0);

    return {
        schemaVersion = 4,
        pluginVersion = VERSION,
        active = active,
        heartbeat = safeCall(function() return Turbine.Engine.GetLocalTime(); end, 0),
        character = safeCall(function() return player:GetName(); end, ""),
        level = safeCall(function() return player:GetLevel(); end, 0),
        classId = classId,
        className = classNames[classId] or "",
        raceId = raceId,
        raceName = raceNames[raceId] or "",
        partySize = getPartySize()
    };
end

local function fingerprint(data)
    return table.concat({
        tostring(data.character),
        tostring(data.level),
        tostring(data.classId),
        tostring(data.className),
        tostring(data.raceId),
        tostring(data.raceName),
        tostring(data.partySize)
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
