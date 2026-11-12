using Slyden.Core.Audio;
using Slyden.Core.Config;

namespace Slyden.Tests;

public class MusicBedTests
{
    // música falsa: batidas a cada 0,5 s (120 BPM) a partir de 0,25 s
    private static BeatAnalysis Song(double seconds, double beat = 0.5, double tempo = 120)
    {
        double[] beats = Enumerable.Range(0, (int)((seconds - 0.25) / beat)).Select(i => 0.25 + i * beat).ToArray();
        return new BeatAnalysis(tempo, beats, beats.Select(_ => 1.0).ToArray(), seconds);
    }

    private static Func<string, BeatAnalysis> Library(params (string Name, double Seconds)[] songs) =>
        path => Song(songs.First(s => s.Name == path).Seconds);

    [Fact]
    public void One_whole_track_passes_through_untouched()
    {
        var bed = MusicBed.Plan([new MusicTrack { Path = "a" }], 3, Library(("a", 60)));
        Assert.True(bed.IsPassThrough);
        Assert.Equal(60, bed.Duration);
        Assert.Equal("a", bed.Render("x.wav"));
    }

    [Fact]
    public void Start_and_end_snap_to_the_nearest_beat()
    {
        var bed = MusicBed.Plan([new MusicTrack { Path = "a", Start = 10.1, End = 30.4 }], 3, Library(("a", 60)));
        var s = bed.Segments[0];
        Assert.False(bed.IsPassThrough);
        Assert.Equal(10.25, s.SourceStart, 6);
        Assert.Equal(30.25, s.SourceEnd, 6);
        Assert.Equal(20, bed.Duration, 6);
        Assert.Equal(0.0, bed.Analysis.BeatTimes[0], 6); // a primeira batida da trilha é o início do corte
    }

    [Fact]
    public void Two_tracks_crossfade_and_the_second_starts_on_a_beat_of_the_first()
    {
        var bed = MusicBed.Plan([new MusicTrack { Path = "a" }, new MusicTrack { Path = "b" }], 3,
            Library(("a", 40), ("b", 30)));
        var (first, second) = (bed.Segments[0], bed.Segments[1]);
        Assert.InRange(second.Offset, first.End - 3.5, first.End - 2.5);
        Assert.Equal(second.FadeIn, first.FadeOut, 6);
        Assert.InRange(first.FadeOut, 2.5, 3.5);
        Assert.Equal(second.End, bed.Duration, 6);
        // o começo da segunda cai numa batida da primeira (diferença múltipla de 0,5 s)
        Assert.Equal(0, (second.Offset - first.Offset - 0.25) % 0.5, 6);
    }

    [Fact]
    public void Combined_beats_are_ordered_without_duplicates_during_the_crossfade()
    {
        var bed = MusicBed.Plan([new MusicTrack { Path = "a" }, new MusicTrack { Path = "b" }], 4,
            Library(("a", 40), ("b", 30)));
        var beats = bed.Analysis.BeatTimes;
        for (int i = 1; i < beats.Length; i++) Assert.True(beats[i] > beats[i - 1] + 1e-9, $"{beats[i - 1]} → {beats[i]}");
        Assert.Equal(beats.Length, bed.Analysis.BeatStrengths.Length);
        Assert.Equal(bed.Duration, bed.Analysis.DurationSeconds, 6);
        Assert.True(beats[^1] > bed.Segments[1].Offset + 20); // as batidas da segunda faixa vão até o fim
    }

    [Fact]
    public void Crossfade_zero_places_tracks_back_to_back()
    {
        var bed = MusicBed.Plan([new MusicTrack { Path = "a" }, new MusicTrack { Path = "b" }], 0,
            Library(("a", 20), ("b", 10)));
        Assert.Equal(20, bed.Segments[1].Offset, 6);
        Assert.Equal(30, bed.Duration, 6);
        Assert.Equal(0, bed.Segments[0].FadeOut);
    }

    [Fact]
    public void Crossfade_never_exceeds_half_of_a_short_track()
    {
        var bed = MusicBed.Plan([new MusicTrack { Path = "a" }, new MusicTrack { Path = "b" }], 10,
            Library(("a", 40), ("b", 4)));
        Assert.True(bed.Segments[1].FadeIn <= 2 + 1e-9);
    }

    [Fact]
    public void Filter_graph_trims_fades_delays_and_mixes_every_segment()
    {
        var bed = MusicBed.Plan([new MusicTrack { Path = "a" }, new MusicTrack { Path = "b", Start = 5.1 }], 3,
            Library(("a", 40), ("b", 30)));
        string graph = bed.FilterGraph();
        Assert.Contains("[0:a]atrim=start=0:end=40", graph);
        Assert.Contains("[1:a]atrim=start=5.25:end=30", graph);
        Assert.Contains("afade=t=out", graph);
        Assert.Contains("afade=t=in", graph);
        Assert.Contains("adelay=", graph);
        Assert.EndsWith("[a0][a1]amix=inputs=2:normalize=0:duration=longest[out]", graph);
    }

    [Fact]
    public void Render_mixes_two_real_files_into_one_with_the_planned_length()
    {
        try { FfmpegLocator.Find(); } catch (FileNotFoundException) { return; } // sem ffmpeg neste computador, não há como testar
        string dir = Path.Combine(Path.GetTempPath(), "bed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string a = Path.Combine(dir, "a.wav"), b = Path.Combine(dir, "b.wav");
            foreach (var (file, freq, secs) in new[] { (a, 440, 12), (b, 660, 10) })
            {
                var psi = new System.Diagnostics.ProcessStartInfo(FfmpegLocator.Find(), $"-y -loglevel error -f lavfi -i sine=frequency={freq}:duration={secs} \"{file}\"")
                { UseShellExecute = false, CreateNoWindow = true };
                using var p = System.Diagnostics.Process.Start(psi)!;
                p.WaitForExit();
            }
            var bed = MusicBed.Plan([new MusicTrack { Path = a, End = 8 }, new MusicTrack { Path = b, Start = 2 }], 2,
                path => Song(path == a ? 12 : 10));
            string output = bed.Render(Path.Combine(dir, "bed.wav"));
            var samples = AudioDecoder.DecodeMono(output);
            Assert.Equal(bed.Duration, samples.Length / (double)BeatAnalyzer.SampleRate, 1);
            Assert.True(samples.Max() > 0.1f); // tem som
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Waveform_peaks_follow_the_loudness_per_slice_and_are_normalised()
    {
        var samples = new float[22050 * 2]; // 2 s: o primeiro segundo baixo (0,25), o segundo alto (0,5)
        for (int i = 0; i < samples.Length; i++) samples[i] = i < 22050 ? 0.25f : -0.5f;
        var peaks = Waveform.Peaks(samples, 22050);
        Assert.InRange(peaks.Length, 2 * Waveform.PerSecond, 2 * Waveform.PerSecond + 1); // 22050/20 não é exato: sobra uma fatia
        Assert.Equal(0.5f, peaks[0], 3);
        Assert.Equal(1f, peaks[^1], 3);
    }

    [Fact]
    public void Tracks_are_validated_and_saved_in_the_config()
    {
        var cfg = new SlideshowConfig { Photos = [new PhotoSpec { File = "1.jpg" }] };
        cfg.Tracks.Add(new MusicTrack { Path = "a.mp3", Start = 3, End = 40 });
        var back = cfg.Clone();
        Assert.Equal((3.0, 40.0), (back.Tracks[0].Start, back.Tracks[0].End!.Value));
        cfg.Tracks[0].End = 2;
        Assert.Throws<InvalidDataException>(cfg.Validate);
    }

    [Fact]
    public void Old_projects_with_one_audio_path_get_a_track()
    {
        string file = Path.Combine(Path.GetTempPath(), $"old-{Guid.NewGuid():N}.slyden");
        try
        {
            File.WriteAllText(file, """{ "name": "Antigo", "audio_path": "C:\\musica.mp3", "config": { "photos": [ { "file": "1.jpg" } ] } }""");
            var project = ProjectStore.Load(file);
            Assert.Equal("C:\\musica.mp3", Assert.Single(project.Config.Tracks).Path);
            Assert.Null(project.AudioPath);
        }
        finally { File.Delete(file); }
    }
}
