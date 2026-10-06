using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;

var assembly = Assembly.LoadFrom(Path.GetFullPath(args[0]));
var type = assembly.GetType("VideoEnhancer.Program")!.GetNestedType("FpsTracker", BindingFlags.NonPublic)!;
var tracker = Activator.CreateInstance(type, [null])!;
var rewrite = type.GetMethod("Rewrite")!;
var fields = BindingFlags.Instance | BindingFlags.NonPublic;
rewrite.Invoke(tracker, ["Total Output Frames: 1000"]);
rewrite.Invoke(tracker, ["FPS: 5 Current Frame: 100 ETA: 0:03:00"]);
type.GetField("_firstLine", fields)!.SetValue(tracker, DateTime.UtcNow - TimeSpan.FromSeconds(20));
Check("FPS: 5 Current Frame: 200 ETA: 0:03:00", 5);
type.GetField("_paused", fields)!.SetValue(tracker, TimeSpan.FromSeconds(5));
Check("FPS: 5 Current Frame: 175 ETA: 0:03:00", 5);
Console.WriteLine("FPS_TRACKER_PASS|initial-frame-offset|pause-time|eta");

void Check(string line, double expected)
{
    var output = (string)rewrite.Invoke(tracker, [line])!;
    var match = Regex.Match(output, @"FPS: ([\d.]+).*ETA: (\d+):(\d+):(\d+)");
    var fps = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
    if (Math.Abs(fps - expected) > 0.01) throw new Exception($"FPS包含计时开始前的帧：{output}");
    var seconds = int.Parse(match.Groups[2].Value) * 3600 + int.Parse(match.Groups[3].Value) * 60 + int.Parse(match.Groups[4].Value);
    var remaining = line.Contains("Frame: 200") ? 800 : 825;
    if (Math.Abs(seconds - remaining / expected) > 1) throw new Exception($"ETA不匹配修正后的FPS：{output}");
}
