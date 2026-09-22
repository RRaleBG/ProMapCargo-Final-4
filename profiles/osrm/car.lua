local find_access_tag = require('lib/access').find_access_tag
local Sequence = require('lib/sequence')
local Set = require('lib/set')
local WayHandlers = require('lib/way_handlers')
local Relations = require('lib/relations')

function setup()
  return {
	properties = {
	  max_speed_for_map_matching      = 110/3.6,
	  use_turn_restrictions           = true,
	  continue_straight_at_waypoint   = true,
	  left_hand_driving               = false,
	  weight_name                     = 'routability',
	  process_call_tagless_node       = false,
	  u_turn_penalty                  = 60,
	  traffic_light_penalty           = 5,
	},

	default_mode              = mode.driving,
	default_speed             = 40,
	oneway_handling           = true,
	side_road_multiplier      = 1.25,
	turn_penalty              = 12,
	speed_reduction           = 0.8,
	cardinal_directions       = false,

	allow_startpoint          = true,
  }
end

local access_tags_hierarchy = Sequence {
  'motorcar',
  'motor_vehicle',
  'vehicle',
  'access'
}

local service_tag_restricted = Set {
  'parking_aisle',
  'driveway',
  'private',
  'emergency_access'
}

local speeds = {
  motorway = 110,
  motorway_link = 60,
  trunk = 90,
  trunk_link = 55,
  primary = 80,
  primary_link = 50,
  secondary = 70,
  secondary_link = 45,
  tertiary = 60,
  tertiary_link = 40,
  unclassified = 45,
  residential = 30,
  service = 20,
  road = 30,
  track = 15,
  living_street = 10
}

local function parse_number(value)
  if not value then
	return nil
  end

  local normalized = string.gsub(value, ',', '.')
  local number = string.match(normalized, '[-%d%.]+')
  if not number then
	return nil
  end

  return tonumber(number)
end

local function is_tag_blocked(value)
  if not value then
	return false
  end

  value = string.lower(value)
  return value == 'no' or value == 'private' or value == 'restricted' or value == 'agricultural'
end

local function is_destination_only(value)
  if not value then
	return false
  end

  value = string.lower(value)
  return value == 'destination' or value == 'delivery' or value == 'customers'
end

function process_way(profile, way, result)
  local highway = way:get_value_by_key('highway')
  if not highway then
	return
  end

  local speed = speeds[highway]
  if not speed then
	return
  end

  local route = way:get_value_by_key('route')
  if route == 'ferry' then
	return
  end

  if service_tag_restricted[way:get_value_by_key('service')] then
	return
  end

  local access = find_access_tag(way, access_tags_hierarchy)

  if is_tag_blocked(access) then
	return
  end

  local maxspeed = parse_number(way:get_value_by_key('maxspeed'))
  if maxspeed and maxspeed > 0 then
	speed = math.min(speed, maxspeed)
  end

  if is_destination_only(access) then
	speed = speed * 0.45
  end

  result.forward_mode = mode.driving
  result.backward_mode = mode.driving
  result.forward_speed = speed
  result.backward_speed = speed
  result.duration = 1

  local oneway = way:get_value_by_key('oneway')
  if oneway == 'yes' or oneway == '1' or oneway == 'true' then
	result.backward_mode = mode.inaccessible
  elseif oneway == '-1' then
	result.forward_mode = mode.inaccessible
  end

  if highway == 'service' or highway == 'track' then
	result.weight = result.weight * 1.8
  end
end

function process_turn(profile, turn)
  turn.duration = turn.duration + profile.turn_penalty
  if turn.is_u_turn then
	turn.duration = turn.duration + profile.properties.u_turn_penalty
  end
  if turn.has_traffic_light then
	turn.duration = turn.duration + profile.properties.traffic_light_penalty
  end

  turn.weight = turn.duration
end
