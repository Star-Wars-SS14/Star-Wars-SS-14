#!/usr/bin/env python3
"""Numeric duel model for SW weapons: time to kill and win chance of two shooters.

Bots are noisy for weapon balance, so this model reads the numbers straight from the YAML prototypes and plays
many duels. It uses only the standard library; run it with `python -I`.

Examples (from the repository root):
    python -I Tools/_SW/sw_duel.py SWE1BlasterRifle SWH206Renegade --armor-a SWRepublicSuit --armor-b SWImperialSuit
    python -I Tools/_SW/sw_duel.py SWK406Ripper SWE1BlasterRifle --distance 12 --hit-b 0.3
    python -I Tools/_SW/sw_duel.py --matrix SWE1BlasterRifle SWH206Renegade SWP301Destroyer SWK406Ripper

What the model reads per weapon: Gun.fireRate, Gun.minAngle (spread), the magazine in the gun_magazine slot
(Battery.maxCharge / BatteryAmmoProvider.fireCost shots, or BallisticAmmoProvider.capacity), the projectile's
Heat/Piercing damage. Per armor: the Armor modifier coefficients. Parents are followed for missing fields.
What it does not model: movement, cover (SWCover), lightsaber reflection, wield bonuses, recoil growth,
damage falloff, stamina. Treat results as a first estimate, then confirm in game.
"""

import argparse
import json
import math
import os
import random
import re
import statistics
import sys

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
PROTO_DIR = os.path.join(REPO, "Resources", "Prototypes")
TICK = 1 / 30  # server tick, the time resolution of a shot

PROTO_START = re.compile(r"^- type: (\w+)\s*$", re.M)


def load_prototypes(root):
    """Very small YAML reader: splits files into top-level prototype blocks and keeps the raw text by id."""
    blocks = {}
    for directory, _, files in os.walk(root):
        for name in files:
            if not name.endswith(".yml"):
                continue
            with open(os.path.join(directory, name), encoding="utf-8", errors="replace") as handle:
                text = handle.read()
            starts = [m.start() for m in PROTO_START.finditer(text)] + [len(text)]
            for begin, end in zip(starts, starts[1:]):
                block = text[begin:end]
                match = re.search(r"^  id: (\S+)\s*$", block, re.M)
                if match and block.startswith("- type: entity"):
                    blocks[match.group(1)] = block
    return blocks


class Protos:
    def __init__(self, blocks):
        self.blocks = blocks

    def parents(self, proto_id):
        block = self.blocks.get(proto_id, "")
        match = re.search(r"^  parent: (.+)$", block, re.M)
        if not match:
            return []
        value = match.group(1).strip()
        if value.startswith("["):
            return [p.strip() for p in value.strip("[]").split(",") if p.strip()]
        return [value]

    def component(self, proto_id, comp, seen=None):
        """The text of a component in this prototype or the nearest parent that has it."""
        seen = seen or set()
        if proto_id in seen or proto_id not in self.blocks:
            return None
        seen.add(proto_id)
        block = self.blocks[proto_id]
        match = re.search(r"^  - type: " + re.escape(comp) + r"\s*$(.*?)(?=^  - type: |\Z)", block, re.M | re.S)
        if match:
            return match.group(1)
        for parent in self.parents(proto_id):
            found = self.component(parent, comp, seen)
            if found is not None:
                return found
        return None

    def field(self, proto_id, comp, key, default=None):
        """A scalar field of a component, looked up through parents when the child does not set it."""
        seen = set()
        stack = [proto_id]
        while stack:
            current = stack.pop(0)
            if current in seen or current not in self.blocks:
                continue
            seen.add(current)
            text = self.component_own(current, comp)
            if text is not None:
                match = re.search(r"^\s+" + re.escape(key) + r":\s*([^\s#]+)", text, re.M)
                if match:
                    return match.group(1)
            stack.extend(self.parents(current))
        return default

    def component_own(self, proto_id, comp):
        block = self.blocks.get(proto_id, "")
        match = re.search(r"^  - type: " + re.escape(comp) + r"\s*$(.*?)(?=^  - type: |\Z)", block, re.M | re.S)
        return match.group(1) if match else None


def weapon_stats(protos, weapon_id):
    if weapon_id not in protos.blocks:
        sys.exit(f"Unknown weapon prototype: {weapon_id}")
    fire_rate = float(protos.field(weapon_id, "Gun", "fireRate", "1"))
    spread = abs(float(protos.field(weapon_id, "Gun", "minAngle", "0")))

    magazine = protos.field(weapon_id, "ItemSlots", "startingItem")
    shots, bullet = None, None
    if magazine:
        bullet = protos.field(magazine, "BatteryAmmoProvider", "proto")
        if bullet:
            charge = float(protos.field(magazine, "Battery", "maxCharge", "0"))
            cost = float(protos.field(magazine, "BatteryAmmoProvider", "fireCost", "100"))
            shots = int(charge // cost) if cost else None
        else:
            capacity = protos.field(magazine, "BallisticAmmoProvider", "capacity")
            shots = int(capacity) if capacity else None
            cartridge = protos.field(magazine, "BallisticAmmoProvider", "proto")
            bullet = protos.field(cartridge, "CartridgeAmmo", "proto") if cartridge else None
    if bullet is None:
        bullet = protos.field(weapon_id, "BatteryAmmoProvider", "proto")

    damage = {}
    if bullet:
        text = protos.component(bullet, "Projectile") or ""
        for kind in ("Heat", "Piercing", "Blunt", "Slash", "Burn"):
            match = re.search(r"^\s+" + kind + r":\s*([\d.]+)", text, re.M)
            if match:
                damage[kind] = float(match.group(1))
    return {
        "id": weapon_id,
        "fire_rate": fire_rate,
        "spread_deg": spread,
        "magazine": magazine,
        "shots_per_magazine": shots,
        "bullet": bullet,
        "damage": damage,
    }


def armor_coefficients(protos, armor_id):
    if not armor_id:
        return {}
    text = protos.component(armor_id, "Armor")
    if text is None:
        sys.exit(f"{armor_id} has no Armor component")
    coefficients = {}
    for kind, value in re.findall(r"^\s+(Heat|Piercing|Blunt|Slash|Burn):\s*([\d.]+)", text, re.M):
        coefficients[kind] = float(value)
    return coefficients


def damage_per_hit(weapon, armor):
    return sum(amount * armor.get(kind, 1.0) for kind, amount in weapon["damage"].items())


def hit_chance(weapon, distance, target_width):
    """Chance that one shot hits a target of target_width tiles at distance tiles, with the spread cone."""
    spread = math.radians(weapon["spread_deg"])
    if spread <= 0:
        return 1.0
    cone_width = 2 * distance * math.tan(spread / 2)
    return min(1.0, target_width / cone_width)


def shot_times(weapon, reaction, reload, horizon):
    """Times at which a shooter fires, with magazine reloads."""
    times, t, in_magazine = [], reaction, 0
    interval = 1 / weapon["fire_rate"]
    shots = weapon["shots_per_magazine"] or 10 ** 9
    while t <= horizon:
        if in_magazine == shots:
            t += reload
            in_magazine = 0
            continue
        times.append(t)
        in_magazine += 1
        t += interval
    return times


def time_to_threshold(times, hit, dmg, threshold, rng):
    total = 0.0
    for t in times:
        if rng.random() < hit:
            total += dmg
            if total >= threshold:
                return math.floor(t / TICK) * TICK
    return math.inf


def duel(a, b, args, rng):
    """Returns (winner, time) for one duel; winner is 'a', 'b' or 'draw'."""
    ta = time_to_threshold(a["times"], a["hit"], a["dmg"], args.threshold, rng)
    tb = time_to_threshold(b["times"], b["hit"], b["dmg"], args.threshold, rng)
    if ta == tb:
        return "draw", ta
    return ("a", ta) if ta < tb else ("b", tb)


def side(protos, weapon_id, armor_of_target, hit_override, args):
    weapon = weapon_stats(protos, weapon_id)
    armor = armor_coefficients(protos, armor_of_target)
    hit = hit_override if hit_override is not None else hit_chance(weapon, args.distance, args.target_width)
    dmg = damage_per_hit(weapon, armor)
    return {
        "weapon": weapon,
        "hit": hit,
        "dmg": dmg,
        "hits_to_threshold": math.ceil(args.threshold / dmg) if dmg > 0 else None,
        "times": shot_times(weapon, args.reaction, args.reload, args.horizon),
    }


def run(protos, weapon_a, weapon_b, args):
    a = side(protos, weapon_a, args.armor_b, args.hit_a, args)
    b = side(protos, weapon_b, args.armor_a, args.hit_b, args)
    rng = random.Random(args.seed)
    wins = {"a": 0, "b": 0, "draw": 0}
    ttk = {"a": [], "b": []}
    for _ in range(args.duels):
        winner, t = duel(a, b, args, rng)
        wins[winner] += 1
        if winner in ttk and t != math.inf:
            ttk[winner].append(t)

    def describe(s, times):
        return {
            "weapon": s["weapon"]["id"],
            "fire_rate": s["weapon"]["fire_rate"],
            "damage_per_hit_after_armor": round(s["dmg"], 2),
            "hits_to_threshold": s["hits_to_threshold"],
            "hit_chance": round(s["hit"], 3),
            "ideal_ttk_s": round((s["hits_to_threshold"] - 1) / s["weapon"]["fire_rate"] + args.reaction, 2)
            if s["hits_to_threshold"] else None,
            "median_ttk_when_winning_s": round(statistics.median(times), 2) if times else None,
            "shots_per_magazine": s["weapon"]["shots_per_magazine"],
        }

    return {
        "a": describe(a, ttk["a"]),
        "b": describe(b, ttk["b"]),
        "win_a": round(wins["a"] / args.duels, 3),
        "win_b": round(wins["b"] / args.duels, 3),
        "draw": round(wins["draw"] / args.duels, 3),
        "settings": {
            "armor_a": args.armor_a,
            "armor_b": args.armor_b,
            "threshold": args.threshold,
            "distance": args.distance,
            "duels": args.duels,
        },
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("weapons", nargs="+", help="weapon prototype IDs: two for a duel, two or more with --matrix")
    parser.add_argument("--matrix", action="store_true", help="every weapon against every other, win chance of row")
    parser.add_argument("--armor-a", default="SWRepublicSuit", help="armor worn by shooter A (default Republic)")
    parser.add_argument("--armor-b", default="SWImperialSuit", help="armor worn by shooter B (default Imperial)")
    parser.add_argument("--threshold", type=float, default=100, help="damage to win: 100 = crit, 200 = death")
    parser.add_argument("--distance", type=float, default=8, help="tiles between the shooters")
    parser.add_argument("--target-width", type=float, default=0.7, help="tiles; a humanoid is about 0.7")
    parser.add_argument("--hit-a", type=float, help="hit chance of A, overrides the spread model")
    parser.add_argument("--hit-b", type=float, help="hit chance of B, overrides the spread model")
    parser.add_argument("--reaction", type=float, default=0.3, help="seconds before the first shot")
    parser.add_argument("--reload", type=float, default=2.0, help="seconds to change a magazine")
    parser.add_argument("--horizon", type=float, default=120, help="seconds before a duel counts as unresolved")
    parser.add_argument("--duels", type=int, default=20000)
    parser.add_argument("--seed", type=int, default=1)
    parser.add_argument("--json", action="store_true", help="print JSON only")
    args = parser.parse_args()

    protos = Protos(load_prototypes(PROTO_DIR))

    if args.matrix:
        rows = {}
        for wa in args.weapons:
            rows[wa] = {}
            for wb in args.weapons:
                rows[wa][wb] = run(protos, wa, wb, args)["win_a"]
        if args.json:
            print(json.dumps(rows, indent=2))
            return
        width = max(len(w) for w in args.weapons) + 2
        print("win chance of the row weapon (armor A) against the column weapon (armor B)")
        print(" " * width + "".join(w[:12].rjust(14) for w in args.weapons))
        for wa in args.weapons:
            print(wa.ljust(width) + "".join(f"{rows[wa][wb]:14.2f}" for wb in args.weapons))
        return

    if len(args.weapons) != 2:
        parser.error("a duel needs exactly two weapons (or use --matrix)")
    result = run(protos, args.weapons[0], args.weapons[1], args)
    if args.json:
        print(json.dumps(result, indent=2))
        return
    for key in ("a", "b"):
        s = result[key]
        print(f"{key.upper()}: {s['weapon']}: {s['damage_per_hit_after_armor']} dmg/hit after armor, "
              f"{s['hits_to_threshold']} hits to {args.threshold:g}, hit chance {s['hit_chance']}, "
              f"ideal TTK {s['ideal_ttk_s']} s, median TTK when winning {s['median_ttk_when_winning_s']} s, "
              f"{s['shots_per_magazine']} shots/magazine")
    print(f"win A {result['win_a']:.1%}, win B {result['win_b']:.1%}, draw {result['draw']:.1%} "
          f"({args.duels} duels at {args.distance:g} tiles)")


if __name__ == "__main__":
    main()
