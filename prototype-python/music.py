"""Slideshow com música real: detecta as batidas e faz cada troca cair numa batida forte.

Uso: python music.py <pasta_fotos> <musica.mp3> <saida.mp4> <config.json>
"""
import json, os, subprocess, sys, wave
import numpy as np
import imageio_ffmpeg
import librosa
from PIL import Image
import slideshow as S

FF = imageio_ffmpeg.get_ffmpeg_exe()


def load_audio(path, tmp_wav, sr=22050):
    subprocess.run([FF, "-y", "-loglevel", "error", "-i", path, "-ac", "1", "-ar", str(sr), tmp_wav], check=True)
    with wave.open(tmp_wav) as w:
        y = np.frombuffer(w.readframes(w.getnframes()), dtype=np.int16).astype(np.float32) / 32768
    return y, sr


def analyse(y, sr):
    env = librosa.onset.onset_strength(y=y, sr=sr)
    tempo, frames = librosa.beat.beat_track(onset_envelope=env, sr=sr)
    times = librosa.frames_to_time(frames, sr=sr)
    return float(np.atleast_1d(tempo)[0]), times, env[frames]


def pick_cuts(times, strength, n_photos, per):
    """Troca a cada `per` batidas, começando na fase (0..per-1) com batidas mais fortes."""
    if per == "auto":
        per = next((p for p in (8, 4, 2) if (len(times) - 1) // p >= n_photos), 2)
    off = max(range(per), key=lambda o: strength[o::per].sum())
    return per, off, list(times[off::per])


def main():
    folder, audio, out, cfg_path = sys.argv[1:5]
    cfg = json.load(open(cfg_path, encoding="utf-8"))
    S.set_format(cfg.get("format", "16:9"))
    specs = S.resolve(cfg)
    tmp = os.path.splitext(out)[0] + "_tmp.wav"
    y, sr = load_audio(audio, tmp)
    os.remove(tmp)
    tempo, times, strength = analyse(y, sr)
    per, off, cuts = pick_cuts(times, strength, len(specs), cfg.get("beats_per_photo", "auto"))
    n = min(len(specs), len(cuts) - 1)
    specs = specs[:n]
    print(f"música {len(y) / sr:.0f}s | tempo ~{tempo:.0f} BPM | {len(times)} batidas | troca a cada {per} (fase {off}) | {n} fotos")

    edges = [round((c - cuts[0]) * S.FPS) for c in cuts[:n + 1]]
    total = (edges[-1]) / S.FPS
    beat = float(np.median(np.diff(times)))
    fade_frames = max(1, round(cfg["default"].get("fade_beats", 0.5) * beat * S.FPS))
    json.dump({"tempo": tempo, "beats": [float(t) for t in times], "cuts": [float(c) for c in cuts[:n + 1]]},
              open(os.path.splitext(out)[0] + "_batidas.json", "w"))

    cmd = [FF, "-y", "-loglevel", "error",
           "-f", "rawvideo", "-pix_fmt", "rgb24", "-s", f"{S.W}x{S.H}", "-r", str(S.FPS), "-i", "-",
           "-ss", f"{cuts[0]:.3f}", "-t", f"{total:.3f}", "-i", audio,
           "-af", f"afade=t=out:st={total - 1.5:.3f}:d=1.5",
           "-c:v", "libx264", "-crf", "20", "-preset", "medium", "-pix_fmt", "yuv420p",
           "-c:a", "aac", "-b:a", "192k", "-shortest", out]
    proc = subprocess.Popen(cmd, stdin=subprocess.PIPE)

    cur = S.Slide(folder, specs[0])
    for i, spec in enumerate(specs):
        nxt = S.Slide(folder, specs[i + 1]) if i + 1 < n else None
        fade_out = nxt is not None and specs[i + 1]["transition"] == "fade"
        frames = edges[i + 1] - edges[i]
        for k in range(frames):
            fr = cur.frame(k / max(frames - 1, 1))
            left = frames - k
            if fade_out and left <= fade_frames:
                fr = Image.blend(fr, nxt.frame(0), 1 - (left - 1) / fade_frames)
            proc.stdin.write(fr.tobytes())
        print(f"{i + 1}/{n} {spec['file'][:4]} {spec['effect']}", flush=True)
        cur = nxt
    proc.stdin.close(); proc.wait()
    print("ok:", out, f"({total:.0f}s)")


if __name__ == "__main__":
    main()
