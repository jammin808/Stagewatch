# Reconnect stampede: N links lose one acceptor at t=0 (process restart: refused connects fail at once)
# and it is back at t=T. Each link redials with base(n) = min(5000, 500*2^n) ms and a jitter rule.
# Measures, over 2000 trials, the most first-successful connects landing in any 250 ms window
# and the time from T until the last link is back.
import random, statistics as st
def base(n): return min(5000, 500 * 2 ** n)
RULES = {
  "pm20 (today: x[0.8,1.2])": lambda n, r: base(n) * (0.8 + 0.4 * r.random()),
  "half (x[0.5,1.0])":        lambda n, r: base(n) * (0.5 + 0.5 * r.random()),
  "full (x[0,1.0])":          lambda n, r: base(n) * r.random(),
  "none (Stagewatch today: 250 doubling)": lambda n, r: min(5000, 250 * 2 ** n),
}
def trial(rule, N, T, r, detect):
    lands = []
    for _ in range(N):
        t = detect(r); n = 0
        while True:
            t += rule(n, r); n += 1
            if t >= T: lands.append(t); break
    lands.sort()
    peak = 0; j = 0
    for i in range(len(lands)):
        while lands[i] - lands[j] > 250: j += 1
        peak = max(peak, i - j + 1)
    return peak, lands[-1] - T
for detect_name, detect in [("reset at t=0", lambda r: 0.0), ("silence: detect in U(10000,12000)", lambda r: 10000 + 2000 * r.random())]:
  for T in (6000, 20000):
    for N in (16, 32):
      print(f"-- {detect_name}, acceptor back after {T} ms, {N} links")
      for name, rule in RULES.items():
        r = random.Random(7)
        res = [trial(rule, N, T if detect_name.startswith("reset") else T + 11000, r, detect) for _ in range(2000)]
        peaks = [p for p, _ in res]; tails = [w for _, w in res]
        print(f"   {name:40s} peak/250ms median {st.median(peaks):4.0f} p95 {sorted(peaks)[1899]:3d} of {N}; all back within median {st.median(tails):5.0f} ms p95 {sorted(tails)[1899]:5.0f} ms")
