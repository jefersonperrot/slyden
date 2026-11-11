"""Protótipo de slideshow: troca de foto na batida, efeitos e transições configuráveis.

Uso: python slideshow.py <pasta_fotos> <saida.mp4> [config.json]
Se config.json não existir, é criado com os padrões para você editar.

Config:
  default  -> vale para todas as fotos (effects, order, intensity, transition, fade_beats)
  photos[] -> cada foto pode sobrescrever: effect, intensity, transition,
              ou (avançado) zoom=[ini,fim] e center=[[x,y],[x,y]]
"""
import json, os, random, subprocess, sys, wave
import numpy as np
import imageio_ffmpeg
from PIL import Image, ImageFilter, ImageOps

W, H, FPS = 1920, 1080, 30
SR = 44100

FORMATS = {"16:9": (1920, 1080), "9:16": (1080, 1920)}


def set_format(name):
    """Define o tamanho do vídeo: 16:9 (YouTube) ou 9:16 (Reels/Stories)."""
    global W, H
    W, H = FORMATS[name]


EFFECTS = ["none", "zoom_in", "zoom_out", "pan_lr", "pan_rl", "tilt_up", "tilt_down"]


def effect_params(name, intensity):
    """Devolve (zoom_ini_fim, centro_ini_fim) para um efeito."""
    z = 1 + intensity
    d = 0.15
    return {
        "none":      ([1, 1], [[.5, .5], [.5, .5]]),
        "zoom_in":   ([1, z], [[.5, .5], [.5, .5]]),
        "zoom_out":  ([z, 1], [[.5, .5], [.5, .5]]),
        "pan_lr":    ([z, z], [[.5 - d, .5], [.5 + d, .5]]),
        "pan_rl":    ([z, z], [[.5 + d, .5], [.5 - d, .5]]),
        "tilt_up":   ([z, z], [[.5, .5 + d], [.5, .5 - d]]),
        "tilt_down": ([z, z], [[.5, .5 - d], [.5, .5 + d]]),
    }[name]


def default_config(folder, bpm=120, beats_per_photo=4):
    files = sorted(f for f in os.listdir(folder) if f.lower().endswith(".jpg"))
    return {
        "bpm": bpm,
        "beats_per_photo": beats_per_photo,
        "format": "16:9",            # 16:9 | 9:16
        "default": {
            "effects": ["zoom_in", "zoom_out", "pan_lr", "pan_rl"],  # 1 item = mesmo efeito em todas
            "order": "cycle",        # cycle | random
            "intensity": 0.15,       # 0.05 sutil ... 0.3 forte
            "transition": "cut",     # cut | fade
            "fade_beats": 0.5,       # duração do fade, em batidas
        },
        "photos": [{"file": f} for f in files],
    }


def resolve(cfg):
    """Aplica o padrão e as exceções de cada foto."""
    d = cfg["default"]
    rng = random.Random(7)
    out = []
    for i, p in enumerate(cfg["photos"]):
        effects = d["effects"]
        auto = effects[i % len(effects)] if d.get("order", "cycle") == "cycle" else rng.choice(effects)
        name = p.get("effect", auto)
        inten = p.get("intensity", d["intensity"])
        zoom, center = effect_params(name, inten)
        out.append({
            "file": p["file"], "effect": name,
            "zoom": p.get("zoom", zoom), "center": p.get("center", center),
            "transition": p.get("transition", d["transition"]),  # transição ao ENTRAR nesta foto
        })
    return out


def ease(t):  # suave no início e no fim
    return t * t * (3 - 2 * t)


def lerp(a, b, t):
    return a + (b - a) * t


def click_track(cuts, beats, total):
    """Clique em cada batida; mais agudo e forte quando a foto troca."""
    n = int(total * SR)
    out = np.zeros(n, dtype=np.float32)
    cutset = {round(c, 4) for c in cuts}
    for b in beats:
        accent = round(b, 4) in cutset
        f, a = (1500, 0.9) if accent else (900, 0.35)
        i = int(b * SR)
        m = min(int(0.04 * SR), n - i)
        if m <= 0:
            continue
        t = np.arange(m) / SR
        out[i:i + m] += (a * np.sin(2 * np.pi * f * t) * np.exp(-t * 90)).astype(np.float32)
    return out


def view_box(img_w, img_h, aspect, z, cx, cy):
    """Retângulo (em coordenadas da foto) com a proporção `aspect`, zoom z, centrado em (cx,cy)."""
    if img_w / img_h > aspect:
        bh = img_h / z
        bw = bh * aspect
    else:
        bw = img_w / z
        bh = bw / aspect
    x0 = min(max(cx * img_w - bw / 2, 0), img_w - bw)
    y0 = min(max(cy * img_h - bh / 2, 0), img_h - bh)
    return (x0, y0, x0 + bw, y0 + bh)


class Slide:
    def __init__(self, folder, spec):
        self.spec = spec
        im = ImageOps.exif_transpose(Image.open(os.path.join(folder, spec["file"]))).convert("RGB")
        self.im = im
        iw, ih = im.size
        self.bg = None
        a = iw / ih
        r = a / (W / H)  # 1 = mesma proporção do quadro
        if 0.79 <= r <= 1.27:  # proporção parecida -> preenche o quadro (corta um pouco)
            self.size, self.offset = (W, H), (0, 0)
        else:  # proporção diferente: foto inteira no centro + fundo desfocado
            fw, fh = (W, int(W / a)) if a >= W / H else (int(H * a), H)
            self.size, self.offset = (fw, fh), ((W - fw) // 2, (H - fh) // 2)
            bg = ImageOps.fit(im, (W // 10, H // 10)).filter(ImageFilter.GaussianBlur(6)).resize((W, H), Image.BICUBIC)
            self.bg = Image.eval(bg, lambda v: int(v * 0.55))

    def frame(self, t):
        (z0, z1), ((x0, y0), (x1, y1)) = self.spec["zoom"], self.spec["center"]
        t = ease(t)
        aspect = self.size[0] / self.size[1]
        box = view_box(self.im.width, self.im.height, aspect, lerp(z0, z1, t), lerp(x0, x1, t), lerp(y0, y1, t))
        fr = self.im.resize(self.size, Image.BICUBIC, box=box)
        if self.bg is not None:
            canvas = self.bg.copy(); canvas.paste(fr, self.offset); fr = canvas
        return fr


def main():
    folder, out = sys.argv[1], sys.argv[2]
    cfg_path = sys.argv[3] if len(sys.argv) > 3 else os.path.join(os.path.dirname(os.path.abspath(out)), "slideshow.json")
    if not os.path.exists(cfg_path):
        with open(cfg_path, "w", encoding="utf-8") as f:
            json.dump(default_config(folder), f, indent=1, ensure_ascii=False)
        print("config criada:", cfg_path)
    cfg = json.load(open(cfg_path, encoding="utf-8"))
    set_format(cfg.get("format", "16:9"))

    beat = 60.0 / cfg["bpm"]
    dur = beat * cfg["beats_per_photo"]
    specs = resolve(cfg)
    total = dur * len(specs)
    frames_per = round(dur * FPS)
    fade_frames = max(1, round(cfg["default"].get("fade_beats", 0.5) * beat * FPS))

    beats = [i * beat for i in range(int(total / beat))]
    cuts = [i * dur for i in range(len(specs))]
    wav = os.path.splitext(out)[0] + "_click.wav"
    pcm = (click_track(cuts, beats, total).clip(-1, 1) * 32767).astype(np.int16)
    with wave.open(wav, "wb") as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR); w.writeframes(pcm.tobytes())

    cmd = [imageio_ffmpeg.get_ffmpeg_exe(), "-y", "-loglevel", "error",
           "-f", "rawvideo", "-pix_fmt", "rgb24", "-s", f"{W}x{H}", "-r", str(FPS), "-i", "-",
           "-i", wav, "-c:v", "libx264", "-crf", "20", "-preset", "medium", "-pix_fmt", "yuv420p",
           "-c:a", "aac", "-b:a", "160k", "-shortest", out]
    proc = subprocess.Popen(cmd, stdin=subprocess.PIPE)

    cur = Slide(folder, specs[0])
    for n, spec in enumerate(specs):
        nxt = Slide(folder, specs[n + 1]) if n + 1 < len(specs) else None
        fade_out = nxt is not None and specs[n + 1]["transition"] == "fade"
        for k in range(frames_per):
            fr = cur.frame(k / (frames_per - 1))
            left = frames_per - k
            if fade_out and left <= fade_frames:
                # o fade termina exatamente na batida: a próxima foto já está 100% visível nela
                a = 1 - (left - 1) / fade_frames
                fr = Image.blend(fr, nxt.frame(0), a)
            proc.stdin.write(fr.tobytes())
        print(f"{n + 1}/{len(specs)} {spec['file'][:4]} {spec['effect']}", flush=True)
        cur = nxt

    proc.stdin.close(); proc.wait()
    print("ok:", out, f"({total:.0f}s)")


if __name__ == "__main__":
    main()
