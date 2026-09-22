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

	vehicle_height            = 4.0,
	vehicle_width             = 2.55,
	vehicle_length            = 16.5,
	vehicle_weight            = 40.0,
	vehicle_axle_load         = 10.0,
	vehicle_is_hgv            = true,
	vehicle_is_goods          = true,
	allow_startpoint          = true,
  }
end

local access_tags_hierarchy = Sequence {
  'motorcar',
  'motor_vehicle',
  'vehicle',
  'access'
}

local truck_access_tags_hierarchy = Sequence {
  'hgv',
  'goods',
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
  motorway = 90,
  motorway_link = 55,
  trunk = 85,
  trunk_link = 50,
  primary = 72,
  primary_link = 45,
  secondary = 62,
  secondary_link = 40,
  tertiary = 50,
  tertiary_link = 35,
  unclassified = 38,
  residential = 26,
  service = 18,
  road = 25,
  track = 12,
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

local function parse_meters(value)
  local number = parse_number(value)
  if not number then
	return nil
  end

  if string.find(string.lower(value), 'ft', 1, true) then
	return number * 0.3048
  end

  return number
end

local function parse_tons(value)
  local number = parse_number(value)
  if not number then
	return nil
  end

  if string.find(string.lower(value), 'kg', 1, true) then
	return number / 1000.0
  end

  return number
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
  local truck_access = find_access_tag(way, truck_access_tags_hierarchy)

  if is_tag_blocked(access) or is_tag_blocked(truck_access) then
	return
  end

  if is_tag_blocked(way:get_value_by_key('hgv')) or is_tag_blocked(way:get_value_by_key('goods')) then
	return
  end

  if is_tag_blocked(way:get_value_by_key('hazmat')) then
	return
  end

  local maxheight = parse_meters(way:get_value_by_key('maxheight'))
  if maxheight and maxheight < profile.vehicle_height then
	return
  end

  local maxwidth = parse_meters(way:get_value_by_key('maxwidth'))
  if maxwidth and maxwidth < profile.vehicle_width then
	return
  end

  local maxlength = parse_meters(way:get_value_by_key('maxlength'))
  if maxlength and maxlength < profile.vehicle_length then
	return
  end

  local maxweight = parse_tons(way:get_value_by_key('maxweight:hgv') or way:get_value_by_key('maxweight'))
  if maxweight and maxweight < profile.vehicle_weight then
	return
  end

  local maxaxleload = parse_tons(way:get_value_by_key('maxaxleload'))
  if maxaxleload and maxaxleload < profile.vehicle_axle_load then
	return
  end

  local maxspeed = parse_number(way:get_value_by_key('maxspeed:hgv') or way:get_value_by_key('maxspeed'))
  if maxspeed and maxspeed > 0 then
	speed = math.min(speed, maxspeed)
  end

  if is_destination_only(access) or is_destination_only(truck_access) then
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