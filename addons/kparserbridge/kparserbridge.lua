--[[
* KParserBridge - Copyright (c) 2026 Sanctum Edition contributors
*
* This program is free software: you can redistribute it and/or modify
* it under the terms of the GNU General Public License as published by
* the Free Software Foundation, either version 2 of the License, or
* (at your option) any later version.
--]]

addon.name      = 'kparserbridge';
addon.author    = 'Sanctum Edition contributors';
addon.version   = '0.1.0';
addon.desc      = 'Writes local party pet-owner mappings for KParser.';
addon.link      = 'https://github.com/Hubris4Life/Kparser-Sanctum-Edition';

require 'common';

local chat = require 'chat';

local bridge =
{
    enabled = true,
    dirty = true,
    nextScan = 0,
    lastSignature = '',
    mappings = {},
};

local packetIds =
{
    [0x000A] = true, -- zone transition
    [0x000D] = true, -- player/entity update
    [0x000E] = true, -- entity update
    [0x0028] = true, -- action
    [0x0067] = true, -- pet/party state
    [0x0068] = true,
    [0x0076] = true, -- party update
    [0x00C8] = true,
    [0x00DD] = true,
};

local function clean(value)
    return (value or '')
        :gsub('[%z\1-\31]', '')
        :gsub('[\t\r\n]', ' ')
        :trim();
end

local function mappingPath()
    return addon.path .. 'data\\pet_mappings.tsv';
end

local function entityAt(index)
    if (index == nil or index <= 0 or index >= 2304) then
        return nil;
    end
    return GetEntity(index);
end

local function getPetIndex(owner)
    if (owner ~= nil) then
        local ok, index = pcall(function () return owner.PetTargetIndex; end);
        if (ok and index ~= nil and index > 0) then
            return index;
        end
    end

    return 0;
end

local function readMappings()
    local party = AshitaCore:GetMemoryManager():GetParty();
    if (party == nil) then
        return {};
    end

    local zone = 0;
    pcall(function () zone = party:GetMemberZone(0) or 0; end);
    local rows = {};
    local seen = {};
    for slot = 0, 17 do
        local ownerIndex = 0;
        pcall(function () ownerIndex = party:GetMemberTargetIndex(slot) or 0; end);
        local owner = entityAt(ownerIndex);
        local ownerName = clean(party:GetMemberName(slot));
        if (owner ~= nil and #ownerName > 0) then
            local petIndex = getPetIndex(owner);
            local pet = entityAt(petIndex);
            if (pet ~= nil) then
                local petName = clean(pet.Name);
                local petServerId = tonumber(pet.ServerId) or 0;
                local ownerServerId = tonumber(owner.ServerId) or 0;
                local key = ('%u|%s|%u|%s'):fmt(
                    petServerId,
                    petName:lower(),
                    ownerServerId,
                    ownerName:lower());
                if #petName > 0 and not seen[key] then
                    seen[key] = true;
                    rows[#rows + 1] =
                    {
                        zone = zone,
                        petServerId = petServerId,
                        petName = petName,
                        ownerServerId = ownerServerId,
                        ownerName = ownerName,
                    };
                end
            end
        end
    end

    table.sort(rows, function (left, right)
        if left.petName:lower() == right.petName:lower() then
            return left.ownerName:lower() < right.ownerName:lower();
        end
        return left.petName:lower() < right.petName:lower();
    end);
    return rows;
end

local function signature(rows)
    local parts = {};
    for _, row in ipairs(rows) do
        parts[#parts + 1] = ('%u:%s:%u:%s'):fmt(
            row.petServerId,
            row.petName,
            row.ownerServerId,
            row.ownerName);
    end
    return table.concat(parts, '|');
end

local function mergeObservedMappings(observed)
    local merged = {};
    local byEntity = {};
    local exact = {};
    for _, row in ipairs(bridge.mappings) do
        merged[#merged + 1] = row;
        if row.petServerId > 0 then
            byEntity[row.petServerId] = #merged;
        end
        exact[('%s|%s'):fmt(row.petName:lower(), row.ownerName:lower())] = true;
    end

    for _, row in ipairs(observed) do
        local existingIndex = row.petServerId > 0 and byEntity[row.petServerId] or nil;
        local exactKey = ('%s|%s'):fmt(row.petName:lower(), row.ownerName:lower());
        if existingIndex ~= nil then
            merged[existingIndex] = row;
        elseif not exact[exactKey] then
            merged[#merged + 1] = row;
            exact[exactKey] = true;
            if row.petServerId > 0 then
                byEntity[row.petServerId] = #merged;
            end
        end
    end

    table.sort(merged, function (left, right)
        if left.petName:lower() == right.petName:lower() then
            return left.ownerName:lower() < right.ownerName:lower();
        end
        return left.petName:lower() < right.petName:lower();
    end);
    return merged;
end

local function writeMappings(rows)
    local finalPath = mappingPath();
    local temporaryPath = finalPath .. '.tmp';
    local file = io.open(temporaryPath, 'w');
    if file == nil then
        return false;
    end

    file:write('# kparserbridge-v1\n');
    file:write('# zone\tpet_server_id\tpet_name\towner_server_id\towner_name\tsource\tconfidence\ttimestamp\n');
    local timestamp = os.date('!%Y-%m-%dT%H:%M:%SZ');
    for _, row in ipairs(rows) do
        file:write(('%u\t%u\t%s\t%u\t%s\tpacket-entity\thigh\t%s\n'):fmt(
            row.zone,
            row.petServerId,
            row.petName,
            row.ownerServerId,
            row.ownerName,
            timestamp));
    end
    file:close();

    os.remove(finalPath);
    if not os.rename(temporaryPath, finalPath) then
        os.remove(temporaryPath);
        return false;
    end
    return true;
end

local function scan(force)
    if not bridge.enabled then
        return;
    end
    local rows = mergeObservedMappings(readMappings());
    local currentSignature = signature(rows);
    if force or currentSignature ~= bridge.lastSignature then
        if writeMappings(rows) then
            bridge.lastSignature = currentSignature;
            bridge.mappings = rows;
        end
    end
    bridge.dirty = false;
    bridge.nextScan = os.time() + 2;
end

local function printStatus()
    local state = bridge.enabled and 'enabled' or 'disabled';
    print(chat.header(addon.name):append(chat.message(
        ('%s; %u current pet mapping(s); local file only.'):fmt(
            state,
            #bridge.mappings))));
end

ashita.events.register('load', 'load_cb', function ()
    bridge.dirty = true;
    scan(true);
end);

ashita.events.register('unload', 'unload_cb', function ()
    scan(true);
end);

ashita.events.register('packet_in', 'packet_in_cb', function (e)
    if packetIds[e.id] then
        bridge.dirty = true;
    end
    if e.id == 0x000A then
        bridge.mappings = {};
        bridge.lastSignature = '';
        writeMappings({});
    end
end);

ashita.events.register('d3d_present', 'present_cb', function ()
    if bridge.enabled and (bridge.dirty or os.time() >= bridge.nextScan) then
        scan(false);
    end
end);

ashita.events.register('command', 'command_cb', function (e)
    local args = e.command:args();
    if #args == 0 or args[1]:lower() ~= '/kparserbridge' then
        return;
    end

    e.blocked = true;
    local action = (#args >= 2 and args[2]:lower()) or 'status';
    if action == 'status' then
        printStatus();
    elseif action == 'rescan' then
        scan(true);
        printStatus();
    elseif action == 'on' then
        bridge.enabled = true;
        scan(true);
        printStatus();
    elseif action == 'off' then
        bridge.enabled = false;
        printStatus();
    elseif action == 'mappings' then
        if #bridge.mappings == 0 then
            print(chat.header(addon.name):append(chat.message('No current party pet mappings.')));
        end
        for _, row in ipairs(bridge.mappings) do
            print(chat.header(addon.name):append(chat.message(
                ('%s -> %s'):fmt(row.petName, row.ownerName))));
        end
    else
        print(chat.header(addon.name):append(chat.message(
            'Commands: status, rescan, mappings, on, off')));
    end
end);
