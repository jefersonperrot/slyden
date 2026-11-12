using Slyden.Core.Audio;
using Slyden.Core.Config;
using Slyden.Core.Render;

namespace Slyden.Tests;

public class OverlaySoundTests
{
    private static SlideshowConfig Config(int photos, params OverlaySound[] sounds)
    {
        var cfg = new SlideshowConfig
        {
            Default = { Effects = [EffectKind.None], Transition = TransitionKind.Cut },
            SecondsPerPhoto = 2,
            Photos = Enumerable.Range(0, photos).Select(i => new PhotoSpec { File = $"{i}.jpg" }).ToList(),
        };
        cfg.Sounds.AddRange(sounds);
        return cfg;
    }

    private static string TempFile()
    {
        string path = Path.GetTempFileName();
        return path;
    }

    [Fact]
    public void A_sound_starts_with_its_photo_plus_the_delay_and_is_cut_at_the_end_of_the_video()
    {
        string file = TempFile();
        try
        {
            var cfg = Config(5, new OverlaySound { Path = file, Photo = 4, Delay = 0.5 }); // foto 4 começa em 6 s; o vídeo tem 10 s
            var plan = SlideshowPlan.Build(cfg, 30, null);
            var placed = AudioMix.Place(cfg, plan, _ => 30); // o arquivo tem 30 s
            var p = Assert.Single(placed);
            Assert.Equal(6.5, p.At, 3);
            Assert.Equal(10 - 6.5, p.Length, 3);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void Sound_trim_and_missing_photos_are_respected()
    {
        string file = TempFile();
        try
        {
            var cfg = Config(3,
                new OverlaySound { Path = file, Photo = 1, Start = 2, End = 5 },
                new OverlaySound { Path = file, Photo = 9 }); // foto que não existe: ignorado
            var plan = SlideshowPlan.Build(cfg, 30, null);
            var p = Assert.Single(AudioMix.Place(cfg, plan, _ => 30));
            Assert.Equal(3, p.Length, 3);
            Assert.Equal(0, p.At, 3);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void Ducking_only_exists_when_the_sound_lowers_the_music()
    {
        var sound = new SoundPlacement(new OverlaySound { Duck = 0.25 }, 10, 5);
        string expression = AudioMix.DuckExpression([sound])!;
        Assert.Contains("0.75", expression); // 1 - 0,25
        Assert.Contains("9.65", expression); // começa a abaixar uma rampa antes
        Assert.Contains("15.35", expression); // volta uma rampa depois
        Assert.Null(AudioMix.DuckExpression([new SoundPlacement(new OverlaySound { Duck = 1 }, 10, 5)]));
    }

    [Fact]
    public void Sounds_are_validated_and_saved()
    {
        var cfg = Config(2, new OverlaySound { Path = "voz.wav", Photo = 2, Delay = 1, Volume = 1.5, Duck = 0.2 });
        var back = cfg.Clone();
        Assert.Equal((2, 1.0, 1.5, 0.2), (back.Sounds[0].Photo, back.Sounds[0].Delay, back.Sounds[0].Volume, back.Sounds[0].Duck));
        cfg.Sounds[0].Photo = 0;
        Assert.Throws<InvalidDataException>(cfg.Validate);
    }

    [Fact]
    public void The_real_mix_has_the_video_length_and_the_music_gets_quieter_under_the_voice()
    {
        try { FfmpegLocator.Find(); } catch (FileNotFoundException) { return; } // sem ffmpeg, não há como testar
        string dir = Path.Combine(Path.GetTempPath(), "mix-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string music = Path.Combine(dir, "musica.wav"), voice = Path.Combine(dir, "voz.wav");
            Sine(music, 220, 20);
            Sine(voice, 880, 2);

            // 10 fotos de 2 s = 20 s sem música no plano; a música entra só como arquivo de áudio
            var cfg = Config(10, new OverlaySound { Path = voice, Photo = 5, Duck = 0.1, Volume = 1 }); // a voz toca de 8 s a 10 s
            var plan = SlideshowPlan.Build(cfg, 30, null);
            var placed = AudioMix.Place(cfg, plan, _ => 2);
            var spec = AudioMix.Spec(cfg, plan, music, placed, firstInput: 0);
            string wav = Path.Combine(dir, "mix.wav");
            AudioMix.RenderWav(spec, wav);

            var samples = AudioDecoder.DecodeMono(wav);
            double rate = BeatAnalyzer.SampleRate;
            Assert.Equal(plan.TotalSeconds, samples.Length / rate, 1);

            double Peak(double from, double to) => samples.Skip((int)(from * rate)).Take((int)((to - from) * rate)).Max(Math.Abs);
            double before = Peak(2, 4);          // só a música
            double during = Peak(8.6, 9.4);       // a voz por cima, música abaixada para 10%
            Assert.True(before > 0.1, $"música audível antes da voz: {before}");
            Assert.True(during > before * 0.8, $"a voz aparece: {during} contra {before}"); // a voz (seno de 880 Hz) é o som dominante
        }
        finally { Directory.Delete(dir, true); }
    }

    private static void Sine(string file, int frequency, int seconds)
    {
        var psi = new System.Diagnostics.ProcessStartInfo(FfmpegLocator.Find(), $"-y -loglevel error -f lavfi -i sine=frequency={frequency}:duration={seconds} \"{file}\"")
        { UseShellExecute = false, CreateNoWindow = true };
        using var p = System.Diagnostics.Process.Start(psi)!;
        p.WaitForExit();
    }
}
