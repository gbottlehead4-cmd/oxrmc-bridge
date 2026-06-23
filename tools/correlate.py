#!/usr/bin/env python3
"""Decode the Sigma SRTA motion stream by correlating it against the WitMotion sensor.

Inputs:
  srta_*.csv    from SrtaSniffer.exe   (unix_ms, src, dst, len, payload_hex)
  sensor_*.csv  from SensorLogger.exe  (unix_ms, roll_deg, pitch_deg, yaw_deg)

Idea: the sensor tells us the rig's REAL roll/pitch at each instant (the answer
key); the packets are the cipher text. For every byte offset in the payload, and
every plausible field type (int16 LE/BE, float32 LE), we build a time series and
measure how strongly it tracks sensor roll and pitch. The offsets that light up
are the encoded axes; the linear fit's slope = unit scale, its sign = polarity.

Run an isolated move per axis (pure roll = steady corner, pure pitch = hard brake,
pure heave = kerb/bump) so each axis stands out cleanly.

Usage:  python correlate.py srta_xxx.csv sensor_xxx.csv
No third-party packages required (pure standard library).
"""
import csv
import math
import struct
import sys


def load_srta(path):
    rows = []
    with open(path, newline="") as f:
        for r in csv.DictReader(f):
            hexstr = r.get("payload_hex", "") or ""
            if not hexstr:
                continue
            rows.append((int(r["unix_ms"]), bytes.fromhex(hexstr)))
    return rows


def load_sensor(path):
    ts, roll, pitch = [], [], []
    with open(path, newline="") as f:
        for r in csv.DictReader(f):
            ts.append(int(r["unix_ms"]))
            roll.append(float(r["roll_deg"]))
            pitch.append(float(r["pitch_deg"]))
    return ts, roll, pitch


def nearest_idx(ts, t):
    lo, hi = 0, len(ts) - 1
    while lo < hi:
        mid = (lo + hi) // 2
        if ts[mid] < t:
            lo = mid + 1
        else:
            hi = mid
    return lo


def pearson(a, b):
    n = len(a)
    if n < 3:
        return 0.0
    ma, mb = sum(a) / n, sum(b) / n
    va = sum((x - ma) ** 2 for x in a)
    vb = sum((y - mb) ** 2 for y in b)
    if va == 0 or vb == 0:
        return 0.0
    cov = sum((a[i] - ma) * (b[i] - mb) for i in range(n))
    return cov / math.sqrt(va * vb)


def slope(x, y):
    """Least-squares slope of y vs x (units per raw count) and offset."""
    n = len(x)
    mx, my = sum(x) / n, sum(y) / n
    vx = sum((xi - mx) ** 2 for xi in x)
    if vx == 0:
        return 0.0, my
    b = sum((x[i] - mx) * (y[i] - my) for i in range(n)) / vx
    return b, my - b * mx


def fields_at(payload, off):
    out = {}
    if off + 2 <= len(payload):
        out["i16le"] = struct.unpack_from("<h", payload, off)[0]
        out["i16be"] = struct.unpack_from(">h", payload, off)[0]
    if off + 4 <= len(payload):
        try:
            out["f32le"] = struct.unpack_from("<f", payload, off)[0]
        except struct.error:
            pass
    return out


def main():
    if len(sys.argv) < 3:
        print(__doc__)
        return
    srta = load_srta(sys.argv[1])
    sts, sroll_all, spitch_all = load_sensor(sys.argv[2])
    if not srta or not sts:
        print("Empty input - did both captures record data?")
        return

    minlen = min(len(p) for _, p in srta)
    # Align sensor roll/pitch onto each packet timestamp.
    sroll, spitch = [], []
    for t, _ in srta:
        i = nearest_idx(sts, t)
        sroll.append(sroll_all[i])
        spitch.append(spitch_all[i])

    results = []
    for off in range(minlen):
        series = {}
        for _, p in srta:
            for k, v in fields_at(p, off).items():
                series.setdefault(k, []).append(float(v))
        for k, vals in series.items():
            if len(vals) != len(srta):
                continue
            cr, cp = pearson(vals, sroll), pearson(vals, spitch)
            axis = "roll" if abs(cr) >= abs(cp) else "pitch"
            ref = sroll if axis == "roll" else spitch
            sl, _ = slope(vals, ref)  # deg per raw unit
            results.append((max(abs(cr), abs(cp)), off, k, axis, cr, cp, sl))

    results.sort(reverse=True)
    print("payload bytes: %d   packets: %d   span: %.1fs"
          % (minlen, len(srta), (srta[-1][0] - srta[0][0]) / 1000.0))
    print("%6s %4s %6s %6s %8s %8s %12s" %
          ("|r|", "off", "type", "axis", "roll_r", "pitch_r", "deg/unit"))
    for score, off, k, axis, cr, cp, sl in results[:25]:
        print("%6.3f %4d %6s %6s %8.3f %8.3f %12.6g" %
              (score, off, k, axis, cr, cp, sl))
    print("\nHigh |r| + a clean deg/unit slope = that axis. Sign of the slope = polarity")
    print("(flip in the writer if OXRMC compensates the wrong way). Repeat per isolated move.")


if __name__ == "__main__":
    main()
