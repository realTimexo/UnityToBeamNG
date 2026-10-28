namespace UnityToBeamNG
{
    /// <summary>Lua files shipped inside the level mod (lamp switching for traffic lights).</summary>
    public static class BeamNGLuaFiles
    {
        public const string ModScriptPath = "scripts/unity_to_beamng/modScript.lua";
        public const string ExtensionPath = "lua/ge/extensions/unityToBeamNG/lights.lua";

        public const string ModScript = @"-- Unity to BeamNG: loads the traffic light lamp extension (only acts on levels that ship signal_lamps.json)
load('unityToBeamNG_lights')
setExtensionUnloadMode('unityToBeamNG_lights', 'manual')
";
        public const string Extension = @"-- Unity to BeamNG: shows the red / yellow / green lamp objects of exported traffic lights.
-- Reads /levels/<level>/signal_lamps.json, asks core_trafficSignals for the current state of each signal and
-- hides/shows the lamp objects (TSStatic) accordingly. Falls back to the exported timing if the state cannot be read.
local M = {}
local TAG = 'unityToBeamNG'
local data = nil
local timer = 0
local clock = 0
local lastState = {}

local function colorFromName(s)
  if type(s) ~= 'string' then return nil end
  s = s:lower()
  if s:find('red') then return 'red' end
  if s:find('yellow') or s:find('amber') then return 'yellow' end
  if s:find('green') then return 'green' end
  return nil
end

local function findState(x, depth)
  depth = depth or 0
  if x == nil or depth > 3 then return nil end
  if type(x) == 'string' then return colorFromName(x) end
  if type(x) == 'table' then
    for _, key in ipairs({'state', 'currentState', 'stateName', 'controller'}) do
      local c = findState(x[key], depth + 1)
      if c then return c end
    end
  end
  return nil
end

local function findSignal(sigName)
  if core_trafficSignals.getSignalByName then
    local s = core_trafficSignals.getSignalByName(sigName)
    if s then return s end
  end
  if core_trafficSignals.getSignals then
    local list = core_trafficSignals.getSignals()
    if type(list) == 'table' then
      for _, s in pairs(list) do
        if type(s) == 'table' and s.name == sigName then return s end
      end
    end
  end
  return nil
end

local function engineState(sigName)
  if not core_trafficSignals then return nil end
  local ok, res = pcall(function()
    local sig = findSignal(sigName)
    if not sig then return nil end
    local st = nil
    if type(sig.getState) == 'function' then st = sig:getState() end
    return findState(st) or findState(sig)
  end)
  if ok then return res end
  return nil
end

local function timelineState(d, t)
  local phase = d.greenSec + d.yellowSec + d.allRedSec
  local cycle = phase * d.phaseCount
  if cycle <= 0 then return 'red' end
  local x = t % cycle
  local idx = math.floor(x / phase)
  if idx ~= d.phaseIndex then return 'red' end
  local r = x - idx * phase
  if r < d.greenSec then return 'green' end
  if r < d.greenSec + d.yellowSec then return 'yellow' end
  return 'red'
end

local function setHidden(name, hidden)
  local o = scenetree.findObject(name)
  if o then pcall(function() o:setHidden(hidden) end) end
end

local function apply(d, color)
  if lastState[d.name] == color then return end
  lastState[d.name] = color
  for _, n in ipairs(d.red or {}) do setHidden(n, color ~= 'red') end
  for _, n in ipairs(d.yellow or {}) do setHidden(n, color ~= 'yellow') end
  for _, n in ipairs(d.green or {}) do setHidden(n, color ~= 'green') end
end

local function loadData()
  data = nil
  lastState = {}
  clock = 0
  if not getCurrentLevelIdentifier then return end
  local id = getCurrentLevelIdentifier()
  if not id then return end
  local path = '/levels/' .. id .. '/signal_lamps.json'
  if not FS:fileExists(path) then return end
  data = jsonReadFile(path)
  log('I', TAG, 'traffic light lamps loaded from ' .. path .. ': ' .. tostring(data and data.signals and #data.signals or 0) .. ' signal(s)')
end

function M.onClientPostStartMission() loadData() end
function M.onClientEndMission() data = nil lastState = {} end
function M.onExtensionLoaded() pcall(loadData) end

function M.onUpdate(dtReal, dtSim, dtRaw)
  if not data or not data.signals then return end
  timer = timer + (dtReal or 0)
  clock = clock + (dtSim or 0)
  if timer < 0.15 then return end
  timer = 0
  for _, d in ipairs(data.signals) do
    apply(d, engineState(d.name) or timelineState(d, clock))
  end
end

return M
";
    }
}
