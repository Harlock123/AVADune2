#!/usr/bin/env python3
"""Synthesises AVADune2's sound effects, voices and music into ../src/AVADune2/Sounds as 16-bit mono WAV.

Nothing is sampled from any game: every effect is built here from square, saw, sine and noise voices. The title plays
the opening of J. S. Bach's Toccata and Fugue in D minor, BWV 565, on a synthesised organ; the battle music, the
victory fanfare and the lament for a lost mission are new compositions. The advisors' words are spoken by the formant
voice in speech.py. Run it from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave

from speech import say


RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVADune2", "Sounds")
RNG = random.Random(1992)


def write(name, samples, volume=0.7):
    peak = max(1e-9, max(abs(s) for s in samples))
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(s / peak * volume * 32767)) for s in samples))


def voice(kind, phase):
    p = phase % 1.0
    if kind == "square":
        return 1.0 if p < 0.5 else -1.0
    if kind == "pulse":
        return 1.0 if p < 0.25 else -1.0
    if kind == "tri":
        return 4 * p - 1 if p < 0.5 else 3 - 4 * p
    if kind == "saw":
        return 2 * p - 1
    if kind == "sine":
        return math.sin(2 * math.pi * p)
    return RNG.uniform(-1, 1)


def tone(seconds, f0, f1=None, kind="square", attack=0.004, release=1.0, vibrato=0.0, vib_rate=8.0, gain=1.0):
    """A note gliding from f0 to f1 (exponentially), with a short attack and a shaped decay."""
    n = int(RATE * seconds)
    f1 = f1 or f0
    out, phase, held = [], 0.0, 0.0
    for i in range(n):
        t = i / RATE
        f = f0 * (f1 / f0) ** (i / max(1, n - 1))
        if vibrato:
            f *= 1 + vibrato * math.sin(2 * math.pi * vib_rate * t)
        phase += f / RATE
        if kind == "noise":
            # Noise re-sampled at the voice's rate: a lower 'pitch' sounds rougher.
            if int(phase) != int(phase - f / RATE):
                held = RNG.uniform(-1, 1)
            s = held
        else:
            s = voice(kind, phase)
        env = min(1.0, t / attack) * (1 - i / n) ** release
        out.append(s * env * gain)
    return out


def lowpass(samples, k):
    """A one-pole filter: k near 0 is dark, near 1 bright."""
    out, y = [], 0.0
    for s in samples:
        y += k * (s - y)
        out.append(y)
    return out


def silence(seconds):
    return [0.0] * int(RATE * seconds)


def cat(*parts):
    out = []
    for p in parts:
        out.extend(p)
    return out


def mix(*parts):
    n = max(len(p) for p in parts)
    out = [0.0] * n
    for p in parts:
        for i, v in enumerate(p):
            out[i] += v
    return out


def echo(samples, delays=((0.07, 0.35), (0.13, 0.22), (0.21, 0.12))):
    """A hall's echoes: copies of the sound a little later and quieter."""
    tail = int(RATE * max(d for d, _ in delays))
    out = samples + [0.0] * tail
    for d, g in delays:
        k = int(RATE * d)
        for i, s in enumerate(samples):
            out[i + k] += s * g
    return out


NOTES = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def hz(note):
    n = NOTES[note[0]]
    rest = note[1:]
    while rest and rest[0] in "#b":
        n += 1 if rest[0] == "#" else -1
        rest = rest[1:]
    return 440.0 * 2 ** ((12 * (int(rest) + 1) + n - 69) / 12)


# ───────────────────────────── instruments ─────────────────────────────

def organ(seconds, note, gain=0.5):
    """A pipe organ: a saw and the octaves above and below, swelling in and dying away slowly."""
    f = hz(note)
    n = int(RATE * seconds)
    out = []
    p1 = p2 = p3 = 0.0
    for i in range(n):
        t = i / RATE
        p1 += f / RATE
        p2 += f * 2 / RATE
        p3 += f / 2 / RATE
        s = 0.45 * voice("saw", p1) + 0.25 * voice("square", p2) * 0.6 + 0.35 * voice("tri", p3)
        env = min(1.0, t / 0.025) * min(1.0, (seconds - t) / 0.06 + 0.0)
        out.append(s * max(0.0, env) * gain)
    return out


def brass(seconds, note, gain=0.5):
    """A brassy voice: a saw that brightens as it swells."""
    f = hz(note)
    n = int(RATE * seconds)
    out, phase, y = [], 0.0, 0.0
    for i in range(n):
        t = i / RATE
        phase += f * (1 + 0.004 * math.sin(2 * math.pi * 5.5 * t)) / RATE
        s = voice("saw", phase)
        k = 0.08 + 0.5 * min(1.0, t / 0.08)
        y += k * (s - y)
        env = min(1.0, t / 0.03) * min(1.0, max(0.0, (seconds - t) / 0.08))
        out.append(y * env * gain)
    return out


def place(buffer, at, samples):
    """Adds a sound into a longer buffer at a time (seconds)."""
    k = int(at * RATE)
    if k + len(samples) > len(buffer):
        buffer.extend([0.0] * (k + len(samples) - len(buffer)))
    for i, s in enumerate(samples):
        buffer[k + i] += s


def kick():
    return mix(tone(0.16, 120, 40, "sine", attack=0.001, release=1.5, gain=1.0), tone(0.02, 3000, 1000, "noise", attack=0.001, release=1, gain=0.3))


def snare():
    return mix(lowpass(tone(0.14, 5000, 2000, "noise", attack=0.001, release=1.4, gain=0.7), 0.5), tone(0.08, 220, 160, "tri", attack=0.001, release=1.2, gain=0.4))


def hat():
    return tone(0.03, 8000, 8000, "noise", attack=0.001, release=1.5, gain=0.25)


# ───────────────────────────── music ─────────────────────────────

def toccata():
    """The opening of Bach's Toccata in D minor, BWV 565: the mordent and fall three times, an octave lower each time;
    the low D and the diminished chord built up over it, resolving; then the running figures, and the last chord."""
    buf = []
    t = 0.0
    q = 0.11
    # Three phrases, each an octave lower.
    for octave, fall in ((5, ["G", "F", "E", "D", "C#"]), (4, ["E", "F", "C#"]), (3, ["G", "F", "E", "D", "C#"])):
        o = str(octave)
        place(buf, t, organ(0.07, "A" + o, 0.5)); t += 0.07
        place(buf, t, organ(0.07, "G" + o, 0.5)); t += 0.07
        place(buf, t, organ(1.5, "A" + o, 0.5)); t += 1.8
        for n in fall:
            place(buf, t, organ(q + 0.03, n + o, 0.5))
            t += q
        place(buf, t, organ(1.6, "D" + o, 0.55))
        t += 2.1
    # The low D, and the diminished chord rising over it, then resolving.
    place(buf, t, organ(5.2, "D2", 0.55))
    place(buf, t, organ(5.2, "D1", 0.4))
    chord = ["C#3", "E3", "G3", "Bb3", "C#4", "E4"]
    for i, n in enumerate(chord):
        place(buf, t + i * 0.28, organ(3.0 - i * 0.28, n, 0.32))
    t += 3.0
    for n in ["D3", "F3", "A3", "D4", "F4"]:
        place(buf, t, organ(2.2, n, 0.32))
    t += 2.4
    # Running figures down and up.
    figures = ["A4", "G4", "F4", "E4", "D4", "C#4", "D4", "E4", "F4", "E4", "D4", "C#4", "D4", "A3", "Bb3", "G3",
               "A3", "F3", "G3", "E3", "F3", "D3", "E3", "C#3", "D3", "F3", "A3", "D4", "F4", "A4", "D5", "C#5",
               "D5", "A4", "F4", "D4", "E4", "C#4", "A3", "C#4", "D4"]
    for n in figures:
        place(buf, t, organ(0.1, n, 0.45))
        t += 0.085
    for n in ["D2", "D3", "A3", "D4", "F4", "A4", "D5"]:
        place(buf, t, organ(3.2, n, 0.3))
    t += 3.4
    return echo(buf)


def battle():
    """The battle music: a driving march in D minor over a pounding bass and drums, four times round with changes."""
    bpm = 132
    beat = 60.0 / bpm
    buf = []
    bass = ["D2", "D2", "D3", "D2", "F2", "D2", "E2", "C#2"] * 2 + ["Bb1", "Bb1", "Bb2", "Bb1", "A1", "A1", "A2", "C#2"] * 2
    lead_a = [("D4", 1), ("F4", 0.5), ("A4", 0.5), ("G4", 1), ("F4", 0.5), ("E4", 0.5), ("D4", 1.5), ("C#4", 0.5), ("D4", 1), ("A3", 1),
              ("Bb3", 1), ("D4", 0.5), ("F4", 0.5), ("E4", 1), ("D4", 0.5), ("C#4", 0.5), ("D4", 2), ("-", 2)]
    lead_b = [("A4", 1.5), ("G4", 0.5), ("F4", 1), ("E4", 1), ("F4", 0.5), ("G4", 0.5), ("A4", 1), ("D5", 2),
              ("C5", 1), ("Bb4", 0.5), ("A4", 0.5), ("G4", 1), ("E4", 1), ("A4", 3), ("-", 1)]
    for rep in range(4):
        start = rep * 32 * beat * 0.5 * 2
        # The bass: eighth notes.
        for i, n in enumerate(bass):
            place(buf, start + i * beat / 2, tone(beat / 2 * 0.9, hz(n), kind="square", attack=0.003, release=0.6, gain=0.28))
        # The drums.
        for b in range(16):
            at = start + b * beat
            place(buf, at, kick() if b % 2 == 0 else snare())
            place(buf, at + beat / 2, hat())
            if rep >= 2 and b % 4 == 3:
                place(buf, at + beat * 0.75, snare())
        # The tune (rested the first time round).
        if rep > 0:
            t = start
            for n, b in (lead_a if rep % 2 == 1 else lead_b):
                if n != "-":
                    place(buf, t, mix(tone(b * beat * 0.95, hz(n), kind="pulse", attack=0.01, release=0.4, vibrato=0.006, vib_rate=6, gain=0.22),
                                      tone(b * beat * 0.95, hz(n) * 1.005, kind="saw", attack=0.01, release=0.4, gain=0.1)))
                t += b * beat
    return buf


def victory():
    beat = 60.0 / 120
    buf = []
    for i, n in enumerate(["D4", "D4", "D4"]):
        place(buf, i * beat / 3, brass(beat / 3 * 0.9, n, 0.5))
    seq = [("F#4", 1), ("A4", 1), ("D5", 0.5), ("A4", 0.5), ("D5", 3)]
    t = beat
    for n, b in seq:
        place(buf, t, brass(b * beat * 0.95, n, 0.5))
        t += b * beat
    for n in ["D3", "A3", "F#4"]:
        place(buf, beat * 3, brass(beat * 3, n, 0.3))
    place(buf, 0, kick())
    place(buf, beat * 3, kick())
    return echo(buf)


def lament():
    beat = 60.0 / 60
    buf = []
    t = 0.0
    for n, b in [("A4", 1), ("F4", 1), ("E4", 0.5), ("D4", 0.5), ("C#4", 1), ("D4", 3)]:
        place(buf, t, organ(b * beat * 0.98, n, 0.45))
        t += b * beat
    for n, at in [("D3", 0), ("A2", 2), ("D2", 4)]:
        place(buf, at * beat, organ(2 * beat, n, 0.35))
    return echo(buf)


# ───────────────────────────── voices ─────────────────────────────

LINES = {
    "SayBuilt": "K AH N S T R AH1 K SH AH N , F IH1 N IH SH T",
    "SayUnitReady": "Y UW1 N IH T , R EH1 D IY",
    "SayAttacked": "DH AH B EY1 S IH Z AH1 N D ER F AY1 ER !",
    "SayReady": "Y EH1 S , K AH M AE1 N D ER",
    "SayMoving": "M UW1 V IH NG AW1 T",
    "SayAttack": "EH N G EY1 JH IH NG",
    "SayLowPower": "P AW1 ER , L OW1",
    "SayStorage": "S T AO1 R IH JH , F UH1 L",
    "SayCreature": "S AE1 N D M AO1 !",
    "SayMissile": "M IH1 S AH L , AH W EY1",
    "SayVictory": "V IH1 K T ER IY , IH Z AW1 ER Z !",
    "SayDefeat": "W IY AA R , D IH F IY1 T IH D !",
}


def radio(samples):
    """A voice over the field radio: a little band-limited, with a click before and a hiss after."""
    dark = lowpass(samples, 0.55)
    bright = [s - d for s, d in zip(samples, lowpass(samples, 0.08))]
    out = [0.6 * a + 0.4 * b for a, b in zip(dark, bright)]
    return cat(tone(0.012, 2500, kind="square", attack=0.001, release=1, gain=0.25), out, tone(0.05, 6000, kind="noise", attack=0.001, release=1, gain=0.06))


def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".wav"):
            os.remove(os.path.join(OUT, f))
    # The interface.
    write("Click.wav", tone(0.03, 1400, 900, "square", attack=0.001, release=1.2, gain=0.6), 0.25)
    write("Refused.wav", cat(tone(0.09, 160, kind="square", release=0.4), silence(0.03), tone(0.12, 110, kind="square", release=0.6)), 0.25)
    write("Place.wav", mix(tone(0.3, 140, 50, "sine", attack=0.002, release=1.5), lowpass(tone(0.25, 900, 300, "noise", attack=0.001, release=1.2, gain=0.7), 0.25)), 0.5)
    write("Credits.wav", tone(0.025, 1900, 2100, "square", attack=0.001, release=1.0, gain=0.5), 0.18)
    # Weapons.
    write("Gun.wav", cat(*[cat(lowpass(tone(0.035, 6000, 1500, "noise", attack=0.0005, release=1.6), 0.6), silence(0.025)) for _ in range(3)]), 0.3)
    write("Cannon.wav", mix(tone(0.35, 95, 38, "sine", attack=0.001, release=1.3), lowpass(tone(0.3, 2000, 200, "noise", attack=0.001, release=1.5, gain=0.9), 0.3)), 0.55)
    write("Rocket.wav", mix(lowpass(tone(0.6, 3000, 600, "noise", attack=0.02, release=0.8), 0.35), tone(0.06, 400, 200, "square", attack=0.001, release=1, gain=0.3)), 0.4)
    write("Boom.wav", lowpass(mix(tone(0.7, 300, 40, "noise", attack=0.002, release=1.4), tone(0.5, 70, 30, "sine", attack=0.002, release=1.2, gain=0.8)), 0.25), 0.6)
    write("BigBoom.wav", lowpass(mix(tone(1.6, 200, 25, "noise", attack=0.004, release=1.2), tone(1.2, 55, 22, "sine", attack=0.004, release=1.0, gain=0.9),
                                     cat(silence(0.15), tone(0.9, 3000, 400, "noise", attack=0.01, release=2, gain=0.3))), 0.2), 0.75)
    write("Sonic.wav", mix(tone(0.8, 260, 900, "sine", attack=0.01, release=0.7, vibrato=0.08, vib_rate=31), tone(0.8, 130, 450, "tri", attack=0.01, release=0.9, vibrato=0.1, vib_rate=23, gain=0.6)), 0.45)
    write("Deviate.wav", mix(lowpass(tone(0.7, 7000, 3000, "noise", attack=0.05, release=0.8, gain=0.6), 0.7), tone(0.3, 600, 1500, "sine", attack=0.005, release=1, gain=0.4)), 0.35)
    write("Crush.wav", mix(lowpass(tone(0.15, 1200, 300, "noise", attack=0.001, release=1.4), 0.4), tone(0.1, 160, 80, "square", attack=0.001, release=1.2, gain=0.3)), 0.35)
    # The battlefield.
    write("Harvest.wav", cat(*[mix(tone(0.07, 90, 70, "square", attack=0.002, release=1.0, gain=0.6), lowpass(tone(0.07, 2500, 800, "noise", attack=0.002, release=1, gain=0.5), 0.4)) for _ in range(7)]), 0.3)
    write("Rumble.wav", lowpass(mix(tone(2.0, 60, 40, "noise", attack=0.3, release=0.8, vibrato=0.3, vib_rate=3),
                                    tone(2.0, 38, 30, "sine", attack=0.3, release=0.8, vibrato=0.1, vib_rate=2, gain=0.8)), 0.12), 0.6)
    write("Swallow.wav", cat(lowpass(mix(tone(0.8, 120, 50, "saw", attack=0.05, release=0.6, vibrato=0.06, vib_rate=12),
                                         tone(0.8, 1500, 300, "noise", attack=0.05, release=0.6, gain=0.6)), 0.2),
                             tone(0.25, 180, 60, "sine", attack=0.005, release=1.2)), 0.65)
    write("Alarm.wav", cat(*[cat(tone(0.14, 880, kind="square", release=0.2, gain=0.5), tone(0.14, 660, kind="square", release=0.2, gain=0.5)) for _ in range(3)]), 0.3)
    write("Bloom.wav", mix(lowpass(tone(0.25, 1500, 200, "noise", attack=0.001, release=1.0), 0.4),
                           cat(silence(0.1), tone(0.9, 4000, 7000, "noise", attack=0.01, release=1.5, gain=0.4)), tone(0.2, 90, 40, "sine", attack=0.001, release=1)), 0.5)
    write("Frigate.wav", lowpass(mix(tone(2.4, 90, 50, "saw", attack=0.4, release=0.8, gain=0.6), tone(2.4, 1200, 400, "noise", attack=0.4, release=0.8)), 0.15), 0.5)
    write("Launch.wav", mix(lowpass(tone(1.8, 400, 3000, "noise", attack=0.05, release=0.7), 0.3), tone(1.5, 60, 120, "saw", attack=0.1, release=0.8, gain=0.5)), 0.55)
    write("Lift.wav", tone(0.7, 180, 220, "pulse", attack=0.05, release=0.8, vibrato=0.3, vib_rate=26, gain=0.6), 0.25)
    # The advisor's voice over the radio.
    for name, words in LINES.items():
        write(name + ".wav", radio(say(words, rate=RATE, pitch=96, speed=0.95, size=1.0)), 0.5)
    # The music.
    write("TitleTune.wav", cat(toccata(), silence(2.0)), 0.36)
    write("Music.wav", battle(), 0.24)
    write("Victory.wav", victory(), 0.4)
    write("GameOver.wav", lament(), 0.42)


if __name__ == "__main__":
    main()
