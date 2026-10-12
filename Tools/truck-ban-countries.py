"""Builds wwwroot/data/truck-ban-countries.geojson: the borders of the truckban.eu countries
(Services/TruckBanRepository.cs, TruckBanCountries) from osm/countries.geojson, simplified to
~1 km (Douglas-Peucker) for the dark red "ban in force" layer of the navigation map.

    python Tools/truck-ban-countries.py
"""
import json
import math
import os

CODES = ["AL", "AT", "BY", "BE", "BA", "BG", "HR", "CZ", "DK", "EE", "FI", "FR", "DE", "GR", "HU", "IE",
         "IT", "XK", "LV", "LI", "LT", "LU", "MK", "MD", "ME", "NL", "NO", "PL", "PT", "RO", "RS", "SK",
         "SI", "ES", "SE", "CH", "TR", "UA", "GB"]
NAMES_SR = {"AL": "Albanija", "AT": "Austrija", "BY": "Belorusija", "BE": "Belgija", "BA": "Bosna i Hercegovina",
            "BG": "Bugarska", "HR": "Hrvatska", "CZ": "Češka", "DK": "Danska", "EE": "Estonija", "FI": "Finska",
            "FR": "Francuska", "DE": "Nemačka", "GR": "Grčka", "HU": "Mađarska", "IE": "Irska", "IT": "Italija",
            "XK": "Kosovo", "LV": "Letonija", "LI": "Lihtenštajn", "LT": "Litvanija", "LU": "Luksemburg",
            "MK": "Severna Makedonija", "MD": "Moldavija", "ME": "Crna Gora", "NL": "Holandija", "NO": "Norveška",
            "PL": "Poljska", "PT": "Portugalija", "RO": "Rumunija", "RS": "Srbija", "SK": "Slovačka", "SI": "Slovenija",
            "ES": "Španija", "SE": "Švedska", "CH": "Švajcarska", "TR": "Turska", "UA": "Ukrajina", "GB": "Velika Britanija"}
TOLERANCE = 0.01   # degrees, ~1 km
MIN_AREA = 0.0005  # drop islets smaller than ~5 km2

root = os.path.join(os.path.dirname(__file__), "..")
source = json.load(open(os.path.join(root, "osm", "countries.geojson"), encoding="utf-8"))["features"]


def simplify(points):
    if len(points) < 3:
        return points
    keep = [False] * len(points)
    keep[0] = keep[-1] = True
    stack = [(0, len(points) - 1)]
    while stack:
        a, b = stack.pop()
        (x1, y1), (x2, y2) = points[a], points[b]
        dx, dy = x2 - x1, y2 - y1
        length = math.hypot(dx, dy)
        best, index = 0.0, -1
        for i in range(a + 1, b):
            x, y = points[i]
            # A closed ring starts and ends at the same point: distance to that point.
            d = abs(dy * x - dx * y + x2 * y1 - y2 * x1) / length if length > 0 else math.hypot(x - x1, y - y1)
            if d > best:
                best, index = d, i
        if best > TOLERANCE:
            keep[index] = True
            stack += [(a, index), (index, b)]
    return [p for p, k in zip(points, keep) if k]


def area(ring):
    return abs(sum(x1 * y2 - x2 * y1 for (x1, y1), (x2, y2) in zip(ring, ring[1:]))) / 2


features = []
for f in source:
    props = f["properties"]
    # Natural Earth has no alpha-2 code ("-99") for these three.
    code = {"Kosovo": "XK", "France": "FR", "Norway": "NO"}.get(props["name"], props.get("ISO3166-1-Alpha-2"))
    if code not in CODES:
        continue
    geometry = f["geometry"]
    polygons = geometry["coordinates"] if geometry["type"] == "MultiPolygon" else [geometry["coordinates"]]
    out = []
    for polygon in polygons:
        rings = [[[round(x, 4), round(y, 4)] for x, y in simplify(ring)] for ring in polygon]
        rings = [r for r in rings if len(r) >= 4]
        if rings and area(rings[0]) >= MIN_AREA:
            out.append(rings)
    if out:
        features.append({"type": "Feature", "properties": {"code": code, "name": NAMES_SR[code]},
                         "geometry": {"type": "MultiPolygon", "coordinates": out}})

missing = sorted(set(CODES) - {f["properties"]["code"] for f in features})
if missing:
    raise SystemExit(f"countries not found: {missing}")

target = os.path.join(root, "wwwroot", "data", "truck-ban-countries.geojson")
os.makedirs(os.path.dirname(target), exist_ok=True)
json.dump({"type": "FeatureCollection", "features": features}, open(target, "w", encoding="utf-8"), separators=(",", ":"))
print(f"{len(features)} countries -> {target} ({os.path.getsize(target) / 1024:.0f} KB)")
