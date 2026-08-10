--[[
* SanctumChat - Copyright (c) 2026 Sanctum Edition contributors
*
* This program is free software: you can redistribute it and/or modify
* it under the terms of the GNU General Public License as published by
* the Free Software Foundation, either version 2 of the License, or
* (at your option) any later version.
--]]

addon.name      = 'sanctumchat';
addon.author    = 'Sanctum Edition contributors';
addon.version   = '0.2.4';
addon.desc      = 'Expands Sanctum pet aliases in chat and pet nameplates.';
addon.link      = 'https://github.com/Hubris4Life/Kparser-Sanctum-Edition';

require 'common';

local chat = require 'chat';

local mappingPrefix = '[SCMAP1]';
local statusPrefix  = '[SCCHAT1]';
local loginRetrySeconds = 5;
local maxNameplateLength = 25;

local sanctumchat =
{
    enabled       = true,
    registered    = false,
    mappings      = {},
    registrationPending = true,
    nextRegistrationAttempt = 0,
};

local function isLoggedIn()
    local party = AshitaCore:GetMemoryManager():GetParty();
    if (party == nil) then
        return false;
    end

    local name = party:GetMemberName(0);
    return name ~= nil and name:trim('\0'):len() > 0;
end

local function queueServerCommand(action)
    if (not isLoggedIn()) then
        return false;
    end

    AshitaCore:GetChatManager():QueueCommand(1, ('!sanctumchat %s'):fmt(action));
    return true;
end

local function cleanProtocolField(value)
    return (value or ''):gsub('[%z\1-\31]+$', ''):trim();
end

local function escapePattern(value)
    return (value:gsub('([^%w])', '%%%1'));
end

local function makeEntityPattern(value)
    local pattern = '%f[%w_]' .. escapePattern(value);
    if (value:match('[%w_]$') ~= nil) then
        pattern = pattern .. '%f[^%w_]';
    end
    return pattern;
end

local function formatPetDisplayName(value)
    local displayName = (value or ''):gsub('_', ' ');
    displayName       = displayName:gsub('(%u)(%u%l)', '%1 %2');
    displayName       = displayName:gsub('([%l%d])(%u)', '%1 %2');
    displayName       = displayName:gsub('%s+', ' '):trim();
    return displayName;
end

local function abbreviatePetName(pet, availableLength)
    if (#pet <= availableLength) then
        return pet;
    end

    if (availableLength <= 1) then
        return pet:sub(1, math.max(availableLength, 0));
    end

    local words = {};
    for word in pet:gmatch('%S+') do
        words[#words + 1] = word;
    end

    if (#words >= 2) then
        local nickname = words[#words];
        local descriptorInitials = {};
        for index = 1, #words - 1 do
            descriptorInitials[#descriptorInitials + 1] = words[index]:sub(1, 1) .. '.';
        end

        local initialName = table.concat(descriptorInitials, ' ') .. ' ' .. nickname;
        if (#initialName <= availableLength) then
            return initialName;
        end

        if (#nickname <= availableLength) then
            return nickname;
        end

        return nickname:sub(1, availableLength - 1) .. '.';
    end

    return pet:sub(1, availableLength - 1) .. '.';
end

local function fitNameplateName(owner, pet)
    local possessivePrefix = ("%s's "):fmt(owner);
    local expanded = possessivePrefix .. pet;
    if (#expanded <= maxNameplateLength) then
        return expanded;
    end

    if (#possessivePrefix < maxNameplateLength) then
        return possessivePrefix .. abbreviatePetName(
            pet,
            maxNameplateLength - #possessivePrefix);
    end

    return expanded:sub(1, maxNameplateLength - 1) .. '.';
end

local function setMapping(alias, owner, pet, entityId)
    alias = cleanProtocolField(alias);
    owner = cleanProtocolField(owner);
    pet   = formatPetDisplayName(cleanProtocolField(pet));

    if (#alias == 0 or #owner == 0 or #pet == 0) then
        return;
    end

    local numericEntityId = tonumber(entityId);
    if (numericEntityId ~= nil) then
        local staleAliases = {};
        for existingAlias, existingMapping in pairs(sanctumchat.mappings) do
            if (existingAlias ~= alias and existingMapping.entityId == numericEntityId) then
                staleAliases[#staleAliases + 1] = existingAlias;
            end
        end

        for _, staleAlias in ipairs(staleAliases) do
            sanctumchat.mappings[staleAlias] = nil;
        end
    end

    local expanded = ("%s's %s"):fmt(owner, pet);
    local nameplate = fitNameplateName(owner, pet);
    sanctumchat.mappings[alias] =
    {
        alias     = alias,
        owner     = owner,
        pet       = pet,
        entityId  = numericEntityId,
        expanded  = expanded,
        nameplate = nameplate,
        pattern   = makeEntityPattern(alias),
        nameplatePattern = makeEntityPattern(nameplate),
    };
end

local function resolveEntity(mapping)
    if (mapping.entityId ~= nil) then
        local index = bit.band(mapping.entityId, 0x0FFF);
        local entity = GetEntity(index);
        if (entity ~= nil and entity.ServerId == mapping.entityId) then
            return index, entity;
        end
    end

    local entityManager = AshitaCore:GetMemoryManager():GetEntity();
    local entityCount = math.min(entityManager:GetEntityMapSize(), 2304);
    for index = 0, entityCount - 1 do
        local entity = GetEntity(index);
        if (entity ~= nil and entity.Name ~= nil and
            cleanProtocolField(entity.Name) == mapping.alias) then
            mapping.entityId = entity.ServerId;
            return index, entity;
        end
    end

    return nil, nil;
end

local function applyNameplates()
    if (not sanctumchat.enabled) then
        return;
    end

    local entityManager = AshitaCore:GetMemoryManager():GetEntity();
    for _, mapping in pairs(sanctumchat.mappings) do
        local index, entity = resolveEntity(mapping);
        if (index ~= nil and entity ~= nil and entity.Name ~= nil and
            cleanProtocolField(entity.Name) ~= mapping.nameplate) then
            entityManager:SetName(index, mapping.nameplate);
        end
    end
end

local function restoreNameplates()
    local entityManager = AshitaCore:GetMemoryManager():GetEntity();
    for _, mapping in pairs(sanctumchat.mappings) do
        local index, entity = resolveEntity(mapping);
        if (index ~= nil and entity ~= nil and entity.Name ~= nil and
            cleanProtocolField(entity.Name) == mapping.nameplate) then
            entityManager:SetName(index, mapping.alias);
        end
    end
end

local function handleProtocolMessage(message)
    local mappingStart = message:find(mappingPrefix, 1, true);
    if (mappingStart ~= nil) then
        local payload = message:sub(mappingStart + #mappingPrefix);
        local alias, owner, pet, entityId = payload:match('^([^|]+)|([^|]+)|([^|]+)|(%d+)$');
        if (alias == nil) then
            alias, owner, pet = payload:match('^([^|]+)|([^|]+)|(.+)$');
        end
        if (alias ~= nil) then
            setMapping(alias, owner, pet, entityId);
        end

        return true;
    end

    local statusStart = message:find(statusPrefix, 1, true);
    if (statusStart ~= nil) then
        local status = cleanProtocolField(message:sub(statusStart + #statusPrefix));
        sanctumchat.registered = status == 'READY';
        if (sanctumchat.registered) then
            sanctumchat.registrationPending = false;
        end
        return true;
    end

    return false;
end

local function expandPetNames(message)
    local changed = message;
    for _, mapping in pairs(sanctumchat.mappings) do
        changed = changed:gsub(mapping.pattern, mapping.expanded);
        if (mapping.nameplate ~= mapping.expanded and mapping.nameplate ~= mapping.alias) then
            changed = changed:gsub(mapping.nameplatePattern, mapping.expanded);
        end
    end

    return changed;
end

local function printHelp()
    local header = chat.header(addon.name);
    print(header:append(chat.message('Commands:')));
    print(header:append(chat.message('/sanctumchat status - Show connection and mapping status.')));
    print(header:append(chat.message('/sanctumchat sync - Refresh current alliance pet mappings.')));
    print(header:append(chat.message('/sanctumchat mappings - List the mappings currently known.')));
    print(header:append(chat.message('/sanctumchat on|off - Enable or disable pet-name expansion.')));
end

ashita.events.register('load', 'load_cb', function ()
    sanctumchat.registrationPending = true;
    sanctumchat.nextRegistrationAttempt = 0;
end);

ashita.events.register('unload', 'unload_cb', function ()
    restoreNameplates();
    if (sanctumchat.registered and isLoggedIn()) then
        queueServerCommand('off');
    end
end);

ashita.events.register('d3d_beginscene', 'beginscene_cb', applyNameplates);

ashita.events.register('packet_in', 'packet_in_cb', function (e)
    if (e.id == 0x000A) then
        sanctumchat.registered             = false;
        sanctumchat.mappings               = {};
        sanctumchat.registrationPending    = true;
        sanctumchat.nextRegistrationAttempt = os.time() + 2;
    end
end);

ashita.events.register('d3d_present', 'present_cb', function ()
    if (not sanctumchat.enabled or
        not sanctumchat.registrationPending or
        os.time() < sanctumchat.nextRegistrationAttempt) then
        return;
    end

    if (queueServerCommand('on')) then
        sanctumchat.registrationPending = false;
    else
        sanctumchat.nextRegistrationAttempt = os.time() + loginRetrySeconds;
    end
end);

ashita.events.register('text_in', 'text_in_cb', function (e)
    if (e.message_modified == nil or #e.message_modified == 0) then
        return;
    end

    if (handleProtocolMessage(e.message_modified)) then
        e.blocked = true;
        return;
    end

    if (sanctumchat.enabled) then
        e.message_modified = expandPetNames(e.message_modified);
    end
end);

ashita.events.register('command', 'command_cb', function (e)
    local args = e.command:args();
    if (#args == 0 or args[1]:lower() ~= '/sanctumchat') then
        return;
    end

    e.blocked = true;
    local action = (#args >= 2 and args[2]:lower()) or 'help';

    if (action == 'help') then
        printHelp();
        return;
    end

    if (action == 'status') then
        local state = sanctumchat.enabled and 'enabled' or 'disabled';
        local connection = sanctumchat.registered and 'registered' or 'waiting for server';
        local count = 0;
        for _ in pairs(sanctumchat.mappings) do
            count = count + 1;
        end

        print(chat.header(addon.name):append(chat.message(
            ('%s; %s; %u pet mapping(s).'):fmt(state, connection, count))));
        return;
    end

    if (action == 'mappings') then
        local count = 0;
        for alias, mapping in pairs(sanctumchat.mappings) do
            count = count + 1;
            print(chat.header(addon.name):append(chat.message(
                ('%s -> %s'):fmt(alias, mapping.expanded))));
        end

        if (count == 0) then
            print(chat.header(addon.name):append(chat.message('No pet mappings are currently known.')));
        end
        return;
    end

    if (action == 'sync') then
        queueServerCommand(sanctumchat.registered and 'sync' or 'on');
        print(chat.header(addon.name):append(chat.message('Pet mapping refresh requested.')));
        return;
    end

    if (action == 'on') then
        sanctumchat.enabled = true;
        if (queueServerCommand('on')) then
            sanctumchat.registrationPending = false;
        else
            sanctumchat.registrationPending = true;
            sanctumchat.nextRegistrationAttempt = os.time() + loginRetrySeconds;
        end
        print(chat.header(addon.name):append(chat.success('Pet-name expansion enabled.')));
        return;
    end

    if (action == 'off') then
        restoreNameplates();
        sanctumchat.enabled    = false;
        sanctumchat.registered = false;
        sanctumchat.mappings   = {};
        sanctumchat.registrationPending = false;
        queueServerCommand('off');
        print(chat.header(addon.name):append(chat.message('Pet-name expansion disabled.')));
        return;
    end

    printHelp();
end);
