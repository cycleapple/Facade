using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Facade;
using Facade.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.LayoutEngine;

int checks = 0;
void Check(bool condition, string message)
{
  if (!condition) throw new InvalidOperationException(message);
  checks++;
}

// The port must not assume API 15's eight festival slots.
LayoutManager layout = new();
Check(layout.ActiveFestivals.Length == 4, "Expected four TC/API 13 festival slots.");
Check(Marshal.SizeOf<OutdoorPlotExteriorData>() == 0x28, "Unexpected exterior layout.");
Check(Marshal.OffsetOf<LayoutManager>(nameof(LayoutManager.OutdoorExteriorData)).ToInt32() == 0x88,
  "Unexpected exterior pointer offset.");

Plot square = new() { C1 = new(0, 0), C2 = new(10, 0), C3 = new(10, 10), C4 = new(0, 10) };
Check(square.IsInside(new(5, 5), 0), "Center must be inside.");
Check(square.IsInside(new(0, 5), 0), "Boundary must be inside.");
Check(!square.IsInside(new(-1, 5), 0), "Outside point must be excluded.");
Check(square.IsInside(new(-1, 5), 3.5f), "Entry padding must restore the facade before collision.");
Check(!square.IsInside(new(-10, 5), 3.5f), "Far point must be excluded with padding.");

JsonSerializerOptions options = new() { Converters = { new Vector2Converter() } };
var districts = JsonSerializer.Deserialize<List<SimplePlot>>(
  File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "plots.json")), options)!;
Check(districts.Count == 10, "Expected five districts with two divisions each.");
foreach (var district in districts)
{
  Check(district.Plots.Count == 30, "Every division must contain 30 plots.");
  foreach (Plot plot in district.Plots)
  {
    Vector2 center = (plot.C1 + plot.C2 + plot.C3 + plot.C4) / 4;
    Check(plot.IsInside(center, 0), $"Plot {district.District}/{plot.PlotId}: center excluded.");
    Check(plot.IsInside(center, 3.5f), $"Plot {district.District}/{plot.PlotId}: padded center excluded.");
    Check(!plot.IsInside(center + new Vector2(1000), 3.5f), "Far outside point accepted.");
  }
}

var packed = UInt128Extensions.Pack(null, 0, 1, 255, 256, 32767, 65534, null);
Check(packed.Unpack() == (null, 0, 1, 255, 256, 32767, 65534, null), "Exterior IDs lost during packing.");
Preset preset = new() { Name = "繁體中文住宅預設", PackedExteriorIds = packed, PackedStainIds = 0, PlotSize = PlotSize.Small };
string json = Newtonsoft.Json.JsonConvert.SerializeObject(preset);
var restored = Newtonsoft.Json.JsonConvert.DeserializeObject<Preset>(json)!;
Check(restored.Name == preset.Name && restored.PackedExteriorIds == packed, "Preset JSON did not round-trip.");
Check(Enum.IsDefined((District)(ushort)339), "API 13 territory ID must resolve without an enum underlying-type mismatch.");
Console.WriteLine($"PASS: {checks} checks (300 plots, API 13 layouts, boundary/padding, preset serialization).");
