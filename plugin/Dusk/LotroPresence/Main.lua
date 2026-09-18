import "Turbine";
import "Turbine.Gameplay";
import "Turbine.UI";

local VERSION = "0.4.11";
local DATA_KEY = "LotroPresence";
local CHECK_INTERVAL = 2;
local HEARTBEAT_INTERVAL = 20;

local player = Turbine.Gameplay.LocalPlayer:GetInstance();
local timer = Turbine.UI.Control();
local lastCheck = 0;
local lastHeartbeat = 0;
local lastFingerprint = nil;
local lastSaveWarning = -60;
local saveInFlight = false;
local inFlightFingerprint = nil;
local queuedSave = nil;
local unloading = false;
local warnedUnknownClasses = {};
local warnedUnknownRaces = {};
local currentRegion = "";
local warnedUnrecognizedRegionalMessage = false;

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
addEnumName(classNames, Turbine.Gameplay.Class, "Corsair", "Marin");
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

local function trim(value)
    if value == nil then
        return "";
    end

    local text = tostring(value);
    text = string.gsub(text, "^%s+", "");
    text = string.gsub(text, "%s+$", "");
    return text;
end

-- Seul le canal Régional est utilisé comme source de localisation.
-- Les canaux Jeu de rôle / RdC / Commerce / Monde peuvent avoir une portée
-- différente et ne doivent pas écraser la région détectée.
local regionalChannelLabels = {
    ["regional"] = true,
    ["régional"] = true
};

local function normalizeChannelLabel(value)
    local normalized = string.lower(trim(value));
    normalized = string.gsub(normalized, "[%.!]+$", "");
    normalized = string.gsub(normalized, "%s+[Cc]hannel$", "");
    normalized = string.gsub(normalized, "%s+[Cc]anal$", "");
    normalized = string.gsub(normalized, "%s+[Kk]anal$", "");
    normalized = string.gsub(normalized, "^[Cc]hannel%s+", "");
    normalized = string.gsub(normalized, "^[Cc]anal%s+", "");
    normalized = string.gsub(normalized, "^[Kk]anal%s+", "");
    return trim(normalized);
end

local function isRegionalChannelLabel(value)
    return regionalChannelLabels[normalizeChannelLabel(value)] == true;
end

local entryPrefixes = {
    "^[Ee]ntered the%s+",
    "^[Yy]ou have entered the%s+",
    "^[Jj]oined the%s+",
    "^[Vv]ous avez rejoint le canal%s+",
    "^[Vv]ous avez rejoint%s+",
    "^[Rr]ejoint le canal%s+",
    "^[Rr]ejoint%s+",
    "^[Ee]ntré dans le canal%s+",
    "^[Ee]ntrée dans le canal%s+",
    "^[Ee]ntré dans%s+",
    "^[Ee]ntrée dans%s+",
    "^[Vv]ous êtes entré dans le canal%s+",
    "^[Vv]ous êtes entrée dans le canal%s+",
    "^[Vv]ous êtes entré dans%s+",
    "^[Vv]ous êtes entrée dans%s+",
    "^[Kk]anal%s+",
    "^[Dd]en Kanal%s+",
    "^[Kk]anal betreten:%s*"
};

local function stripEntryPrefix(value)
    local result = trim(value);

    for index = 1, table.getn(entryPrefixes) do
        local stripped, count = string.gsub(result, entryPrefixes[index], "", 1);
        if count > 0 then
            result = trim(stripped);
            break;
        end
    end

    -- Certaines localisations placent explicitement "canal/channel/kanal"
    -- juste avant le nom de région.
    local afterKeyword = string.match(result, ".*[Cc]anal%s+(.+)$");
    if afterKeyword == nil then
        afterKeyword = string.match(result, ".*[Cc]hannel%s+(.+)$");
    end
    if afterKeyword == nil then
        afterKeyword = string.match(result, ".*[Kk]anal%s+(.+)$");
    end
    if afterKeyword ~= nil and trim(afterKeyword) ~= "" then
        result = trim(afterKeyword);
    end

    return result;
end

local function isLeaveMessage(message)
    local lowered = string.lower(message);

    return string.find(lowered, "left the", 1, true) ~= nil or
           string.find(lowered, "you have left", 1, true) ~= nil or
           string.find(lowered, "quitt", 1, true) ~= nil or
           string.find(lowered, "sorti du canal", 1, true) ~= nil or
           string.find(lowered, "sortie du canal", 1, true) ~= nil or
           string.find(lowered, "verlassen", 1, true) ~= nil;
end

local function extractRegionFromDescriptor(descriptor)
    descriptor = trim(descriptor);
    descriptor = string.gsub(descriptor, "[%.!]+$", "");
    descriptor = string.gsub(descriptor, "%s+[Cc]hannel$", "");
    descriptor = string.gsub(descriptor, "%s+[Cc]anal$", "");
    descriptor = string.gsub(descriptor, "%s+[Kk]anal$", "");

    -- Le côté gauche est volontairement gourmand afin de couper sur le dernier
    -- " - " : certains noms propres peuvent eux-mêmes contenir un tiret.
    local left, right = string.match(descriptor, "^(.*)%s+%-%s+(.+)$");
    if left == nil or right == nil then
        return nil;
    end

    left = trim(left);
    right = trim(right);

    if isRegionalChannelLabel(right) and not isRegionalChannelLabel(left) then
        local region = stripEntryPrefix(left);
        if region ~= "" then
            return region;
        end
    end

    if isRegionalChannelLabel(left) and not isRegionalChannelLabel(right) then
        local region = stripEntryPrefix(right);
        if region ~= "" then
            return region;
        end
    end

    return nil;
end

local function extractRegionFromChatMessage(message)
    message = trim(message);
    if message == "" or isLeaveMessage(message) then
        return nil;
    end

    -- Format FR observé directement en jeu :
    -- "Canal Bree - Régional : connexion."
    -- On le traite en priorité car c'est le signal le plus fiable sur client FR.
    local frenchRegion = string.match(
        message,
        "^[Cc]anal%s+(.+)%s+%-%s+[Rr]égional%s*:%s*[Cc]onnexion[%.!]*$"
    );
    if frenchRegion ~= nil and trim(frenchRegion) ~= "" then
        return trim(frenchRegion);
    end

    -- Format anglais observé par d'autres plugins LOTRO :
    -- "Entered the Ered Luin - Regional channel."
    -- Le parseur général ci-dessous accepte aussi des variantes proches,
    -- mais uniquement si le descripteur correspond au canal Régional.
    local region = extractRegionFromDescriptor(message);
    if region ~= nil then
        return region;
    end

    -- Quelques clients/localisations peuvent placer "channel/canal" ailleurs.
    local descriptor = string.match(message, "^[Ee]ntered the%s+(.+)%s+channel[%.!]*$");
    if descriptor == nil then
        descriptor = string.match(message, "^[Yy]ou have entered the%s+(.+)%s+channel[%.!]*$");
    end
    if descriptor == nil then
        descriptor = string.match(message, "^[Vv]ous avez rejoint le canal%s+(.+)[%.!]*$");
    end
    if descriptor == nil then
        descriptor = string.match(message, "^[Ee]ntré dans le canal%s+(.+)[%.!]*$");
    end
    if descriptor == nil then
        descriptor = string.match(message, "^[Ee]ntrée dans le canal%s+(.+)[%.!]*$");
    end
    if descriptor == nil then
        descriptor = string.match(message, "^[Kk]anal%s+(.+)%s+betreten[%.!]*$");
    end

    if descriptor == nil then
        return nil;
    end

    return extractRegionFromDescriptor(descriptor);
end

local function addChatHandler(handler)
    if type(Turbine.Chat.Received) == "table" then
        table.insert(Turbine.Chat.Received, handler);
    elseif type(Turbine.Chat.Received) == "function" then
        Turbine.Chat.Received = { Turbine.Chat.Received, handler };
    else
        Turbine.Chat.Received = { handler };
    end
end

local function removeChatHandler(handler)
    if type(Turbine.Chat.Received) == "table" then
        for index = table.getn(Turbine.Chat.Received), 1, -1 do
            if Turbine.Chat.Received[index] == handler then
                table.remove(Turbine.Chat.Received, index);
            end
        end
    elseif Turbine.Chat.Received == handler then
        Turbine.Chat.Received = nil;
    end
end

local function onChatReceived(sender, args)
    if args == nil or args.ChatType ~= Turbine.ChatType.Standard then
        return;
    end

    local message = args.Message;
    local region = extractRegionFromChatMessage(message);
    if region ~= nil and region ~= "" then
        if region ~= currentRegion then
            currentRegion = region;
            pcall(function()
                Turbine.Shell.WriteLine(
                    "LotroPresence : région détectée : " .. tostring(region)
                );
            end);
        end
        return;
    end

    -- Diagnostic léger pour l'alpha : si LOTRO émet bien un message régional
    -- mais dans une forme encore inconnue, on l'affiche une seule fois.
    if not warnedUnrecognizedRegionalMessage and message ~= nil then
        local lowered = string.lower(tostring(message));
        if string.find(lowered, "regional", 1, true) ~= nil or
           string.find(lowered, "régional", 1, true) ~= nil or
           string.find(lowered, "canal ", 1, true) ~= nil then
            warnedUnrecognizedRegionalMessage = true;
            pcall(function()
                Turbine.Shell.WriteLine(
                    "LotroPresence : message régional non reconnu : " .. tostring(message)
                );
            end);
        end
    end
end

local previousData = safeCall(function()
    return Turbine.PluginData.Load(Turbine.DataScope.Character, DATA_KEY);
end, nil);
if type(previousData) == "table" and type(previousData.region) == "string" then
    currentRegion = trim(previousData.region);
end

local function warnUnknown(kind, id, warned)
    if id == nil or id == 0 or warned[id] then
        return;
    end

    warned[id] = true;
    pcall(function()
        Turbine.Shell.WriteLine(
            "LotroPresence : " .. kind .. " inconnue (ID " .. tostring(id) .. "). " ..
            "La présence continue avec les informations disponibles."
        );
    end);
end

local function warnSave(now, message)
    if (now - lastSaveWarning) < 60 then
        return;
    end

    lastSaveWarning = now;
    pcall(function()
        local suffix = "";
        if message ~= nil and tostring(message) ~= "" then
            suffix = " (" .. tostring(message) .. ")";
        end

        Turbine.Shell.WriteLine(
            "LotroPresence : impossible d'écrire PluginData" .. suffix ..
            ". Nouvelle tentative automatique."
        );
    end);
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
    local className = classNames[classId] or "";
    local raceName = raceNames[raceId] or "";

    if className == "" then
        warnUnknown("classe", classId, warnedUnknownClasses);
    end

    if raceName == "" then
        warnUnknown("race", raceId, warnedUnknownRaces);
    end

    return {
        schemaVersion = 4,
        pluginVersion = VERSION,
        active = active,
        heartbeat = safeCall(function() return Turbine.Engine.GetLocalTime(); end, 0),
        character = safeCall(function() return player:GetName(); end, ""),
        level = safeCall(function() return player:GetLevel(); end, 0),
        classId = classId,
        className = className,
        raceId = raceId,
        raceName = raceName,
        partySize = getPartySize(),
        region = currentRegion
    };
end

local function fingerprint(data)
    return table.concat({
        tostring(data.active),
        tostring(data.character),
        tostring(data.level),
        tostring(data.classId),
        tostring(data.className),
        tostring(data.raceId),
        tostring(data.raceName),
        tostring(data.partySize),
        tostring(data.region)
    }, "|");
end

local savePluginData;
local saveFinalInactiveSnapshot;
savePluginData = function(data, currentFingerprint, now)
    if saveInFlight then
        if currentFingerprint ~= inFlightFingerprint then
            queuedSave = {
                data = data,
                fingerprint = currentFingerprint
            };
        end
        return false;
    end

    saveInFlight = true;
    inFlightFingerprint = currentFingerprint;

    local ok, callError = pcall(function()
        Turbine.PluginData.Save(
            Turbine.DataScope.Character,
            DATA_KEY,
            data,
            function(succeeded, message)
                local completionNow = safeCall(function()
                    return Turbine.Engine.GetGameTime();
                end, now);

                saveInFlight = false;
                inFlightFingerprint = nil;

                if succeeded then
                    lastFingerprint = currentFingerprint;
                    lastHeartbeat = completionNow;
                else
                    warnSave(completionNow, message);
                end

                if unloading then
                    queuedSave = nil;
                    if saveFinalInactiveSnapshot ~= nil then
                        saveFinalInactiveSnapshot();
                    end
                    return;
                end

                local pending = queuedSave;
                queuedSave = nil;

                if pending ~= nil and pending.fingerprint ~= lastFingerprint then
                    savePluginData(
                        pending.data,
                        pending.fingerprint,
                        completionNow
                    );
                end
            end
        );
    end);

    if not ok then
        saveInFlight = false;
        inFlightFingerprint = nil;
        warnSave(now, callError);
        return false;
    end

    return true;
end

local function saveSnapshot(forceHeartbeat, active)
    if unloading then
        return;
    end

    local data = buildSnapshot(active);
    local currentFingerprint = fingerprint(data);
    local now = Turbine.Engine.GetGameTime();

    if forceHeartbeat or currentFingerprint ~= lastFingerprint or (now - lastHeartbeat) >= HEARTBEAT_INTERVAL then
        savePluginData(data, currentFingerprint, now);
    end
end

saveFinalInactiveSnapshot = function()
    unloading = true;
    queuedSave = nil;
    local data = buildSnapshot(false);
    local now = safeCall(function() return Turbine.Engine.GetGameTime(); end, 0);

    -- À l'Unload, on lance toujours une écriture active=false. Si une sauvegarde
    -- active plus ancienne termine ensuite et que son callback s'exécute encore,
    -- celui-ci réaffirme immédiatement active=false afin de réduire la course
    -- d'ordre entre les écritures asynchrones.
    local ok, callError = pcall(function()
        Turbine.PluginData.Save(
            Turbine.DataScope.Character,
            DATA_KEY,
            data,
            function(succeeded, message)
                if not succeeded then
                    warnSave(now, message);
                end
            end
        );
    end);

    if not ok then
        warnSave(now, callError);
    end
end

addChatHandler(onChatReceived);

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
        removeChatHandler(onChatReceived);
        saveFinalInactiveSnapshot();
    end;
end

Turbine.Shell.WriteLine("LotroPresence " .. VERSION .. " chargé.");
