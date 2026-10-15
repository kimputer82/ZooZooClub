"""Zoo Zoo Club 효과음을 합성해서 Resources/Audio/sfx_*.wav 로 저장한다."""
import numpy as np, os, sys
from scipy.io import wavfile
from scipy.signal import butter, lfilter

SR = 44100
OUT = sys.argv[1] if len(sys.argv) > 1 else '.'

def t(dur): return np.arange(int(SR * dur)) / SR
def env(n, a=0.005, d=0.1, s=0.0, r=0.05, hold=None):
    """ADSR 비슷한 간단 포락선."""
    a_n, d_n, r_n = int(a*SR), int(d*SR), int(r*SR)
    e = np.ones(n)
    e[:a_n] = np.linspace(0, 1, a_n)
    if d_n: e[a_n:a_n+d_n] = np.linspace(1, s if s else 1, d_n)[:max(0, n-a_n)]
    if s: e[a_n+d_n:] = s
    tail = np.linspace(1, 0, min(r_n, n)) ; e[n-len(tail):] *= tail
    return e
def tone(freq, dur, kind='sine', vib=0.0):
    x = t(dur)
    ph = 2*np.pi*freq*x + (vib*np.sin(2*np.pi*6*x) if vib else 0)
    if kind == 'sine': return np.sin(ph)
    if kind == 'square': return np.sign(np.sin(ph)) * 0.6
    if kind == 'tri': return (2/np.pi)*np.arcsin(np.sin(ph))
    if kind == 'saw': return 2*((freq*x) % 1) - 1
def sweep(f0, f1, dur, kind='sine'):
    x = t(dur); f = np.linspace(f0, f1, len(x)); ph = 2*np.pi*np.cumsum(f)/SR
    return np.sin(ph) if kind == 'sine' else (2/np.pi)*np.arcsin(np.sin(ph))
def noise(dur): return np.random.default_rng(7).uniform(-1, 1, int(SR*dur))
def lowpass(x, fc, order=2):
    b, a = butter(order, fc/(SR/2)); return lfilter(b, a, x)
def seq(parts, gap=0.0):
    out = []
    for p in parts: out.append(p); out.append(np.zeros(int(SR*gap)))
    return np.concatenate(out)
def mix(*xs):
    n = max(len(x) for x in xs); y = np.zeros(n)
    for x in xs: y[:len(x)] += x
    return y
def save(name, x, peak=0.5):
    x = x / (np.max(np.abs(x)) + 1e-9) * peak
    x = lowpass(x, 9000)
    wavfile.write(os.path.join(OUT, name), SR, (x * 32767).astype(np.int16))
    print(name, f'{len(x)/SR:.2f}s')

def note(freq, dur, kind='tri', a=0.004, r=0.04, s=0):
    x = tone(freq, dur, kind); return x * env(len(x), a=a, d=dur*0.5, s=s, r=r)

# 버튼 클릭: 짧고 부드러운 톡
click = sweep(900, 600, 0.06) * env(int(SR*0.06), a=0.002, d=0.03, r=0.02)
save('sfx_click.wav', click, 0.35)

# 수확: 톡 하고 뽑히는 느낌(노이즈 + 올라가는 두 음)
harvest = mix(noise(0.05) * env(int(SR*0.05), a=0.001, d=0.03, r=0.02) * 0.5,
              seq([note(523, 0.08), note(784, 0.14)]))
save('sfx_harvest.wav', harvest, 0.45)

# 코인: 띠링
coin = seq([note(1047, 0.05, 'square'), note(1319, 0.28, 'square', r=0.2)])
save('sfx_coin.wav', coin, 0.35)

# 정답: 올라가는 아르페지오
correct = seq([note(523, 0.09, 'tri'), note(659, 0.09, 'tri'), note(784, 0.09, 'tri'), note(1047, 0.3, 'tri', r=0.25)])
save('sfx_correct.wav', correct, 0.45)

# 오답: 낮게 두 번 부웅
wrong = seq([note(220, 0.16, 'saw', r=0.05), note(175, 0.3, 'saw', r=0.2)], gap=0.02)
save('sfx_wrong.wav', lowpass(wrong, 1500), 0.4)

# 침략 경보: 두 음을 오가는 사이렌
raid = seq([note(620, 0.16, 'square', a=0.01, r=0.03), note(440, 0.16, 'square', a=0.01, r=0.03)] * 3, gap=0.01)
save('sfx_raid.wav', lowpass(raid, 3000), 0.4)

# 건설: 망치질 세 번(노이즈 + 낮은 몸통)
tap = mix(noise(0.07) * env(int(SR*0.07), a=0.001, d=0.04, r=0.03), note(140, 0.12, 'sine', r=0.08))
build = seq([tap, tap, tap * 1.2], gap=0.08)
save('sfx_build.wav', lowpass(build, 2500), 0.5)

# 승리/레벨 업: 짧은 팡파르
fan = seq([note(523, 0.1, 'square'), note(659, 0.1, 'square'), note(784, 0.1, 'square'),
           mix(note(1047, 0.5, 'tri', r=0.4), note(784, 0.5, 'tri', r=0.4) * 0.5, note(659, 0.5, 'tri', r=0.4) * 0.4)])
save('sfx_win.wav', fan, 0.45)
