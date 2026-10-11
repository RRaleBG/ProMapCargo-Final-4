"""Builds the map-cut polygon of a region: the listed countries from countries.geojson
(ISO alpha-2 code or exact name, e.g. "Kosovo") plus optional rectangles
"bbox:minLon,minLat,maxLon,maxLat". Output: a GeoJSON FeatureCollection for `pmtiles extract --region`.

    python region-geojson.py osm/countries.geojson out.geojson RS BA Kosovo bbox:7.5,47.2,13.9,50.6
"""
import json
import sys

source, target, *parts = sys.argv[1:]
countries = json.load(open(source, encoding="utf-8"))["features"]
features = []

for part in parts:
    if part.startswith("bbox:"):
        x0, y0, x1, y1 = (float(v) for v in part[5:].split(","))
        ring = [[x0, y0], [x1, y0], [x1, y1], [x0, y1], [x0, y0]]
        features.append({"type": "Feature", "properties": {"name": part}, "geometry": {"type": "Polygon", "coordinates": [ring]}})
        continue

    match = [f for f in countries
             if f["properties"].get("ISO3166-1-Alpha-2") == part or f["properties"].get("name") == part]
    if not match:
        sys.exit(f"country not found: {part}")
    features.extend(match)

json.dump({"type": "FeatureCollection", "features": features}, open(target, "w", encoding="utf-8"))
print(f"region polygon: {len(features)} parts -> {target}")
