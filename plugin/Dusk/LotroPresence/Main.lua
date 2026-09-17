import "Turbine";
import "Turbine.Gameplay";
import "Turbine.UI";

local VERSION = "0.2.0";
local DATA_KEY = "LotroPresence";
local CHECK_INTERVAL = 2;
local HEARTBEAT_INTERVAL = 10;

local player = Turbine.Gameplay.LocalPlayer:GetInstance();
local timer = Turbine.UI.Control();
local lastCheck = 0;
local lastHeartbeat = 0;
local lastFingerprint = nil;
local currentZone = "";

local function trim(value)
    if value == nil then
        return "";
    end

    return string.gsub(string.gsub(tostring(value), "^%s+", ""), "%s+$", "");
end

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

local function safeCall(fn, fallback)
    local ok, value = pcall(fn);
    if ok and value ~= nil then
        return value;
    end
    return fallback;
end

local previousData = safeCall(function()
    return Turbine.PluginData.Load(Turbine.DataScope.Character, DATA_KEY);
end, nil);

if type(previousData) == "table" and type(previousData.zoneName) == "string" then
    currentZone = previousData.zoneName;
end

local function buildSnapshot(active)
    local classId = safeCall(function() return player:GetClass(); end, 0);

    return {
        schemaVersion = 2,
        pluginVersion = VERSION,
        active = active,
        heartbeat = safeCall(function() return Turbine.Engine.GetLocalTime(); end, 0),
        character = safeCall(function() return player:GetName(); end, ""),
        level = safeCall(function() return player:GetLevel(); end, 0),
        classId = classId,
        className = classNames[classId] or "",
        zoneName = currentZone
    };
end

local function fingerprint(data)
    return table.concat({
        tostring(data.character),
        tostring(data.level),
        tostring(data.classId),
        tostring(data.className),
        tostring(data.zoneName)
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

local function parseLocZone(args)
    args = trim(args);
    if args == "" then
        return nil;
    end

    local parts = {};
    for part in string.gmatch(args, "([^:]+)") do
        table.insert(parts, trim(part));
    end

    -- ;loc donne typiquement "Eriador: Bree: 31.9S, 50.4W".
    -- On garde le dernier nom situé avant les coordonnées.
    if table.getn(parts) >= 2 then
        local candidate = parts[table.getn(parts) - 1];
        if candidate ~= "" then
            return candidate;
        end
    end

    return nil;
end

local presenceCommand = Turbine.ShellCommand();

function presenceCommand:Execute(command, args)
    local text = trim(args);
    local lower = string.lower(text);

    if lower == "clear" then
        currentZone = "";
        saveSnapshot(true, true);
        Turbine.Shell.WriteLine("LotroPresence : zone effacée.");
        return;
    end

    if string.sub(lower, 1, 5) == "zone " then
        local manualZone = trim(string.sub(text, 6));
        if manualZone ~= "" then
            currentZone = manualZone;
            saveSnapshot(true, true);
            Turbine.Shell.WriteLine("LotroPresence : zone = " .. currentZone);
        end
        return;
    end

    local detectedZone = parseLocZone(text);
    if detectedZone ~= nil then
        currentZone = detectedZone;
        saveSnapshot(true, true);
        Turbine.Shell.WriteLine("LotroPresence : zone = " .. currentZone);
        return;
    end

    Turbine.Shell.WriteLine("LotroPresence : utilise /lp ;loc pour actualiser la zone.");
end

function presenceCommand:GetHelp()
    return "/lp ;loc : actualise la zone. /lp zone <nom> : définit la zone manuellement. /lp clear : efface la zone.";
end

function presenceCommand:GetShortHelp()
    return "Actualise la zone utilisée par LotroPresence.";
end

Turbine.Shell.AddCommand("lp", presenceCommand);

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
        Turbine.Shell.RemoveCommand(presenceCommand);
        saveSnapshot(true, false);
    end;
end

Turbine.Shell.WriteLine("LotroPresence " .. VERSION .. " chargé. Zone : /lp ;loc");
