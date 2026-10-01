namespace Facade;

internal static class ZhTw
{
  public static string DistrictName(District district) => district switch
  {
    District.Mist => "海霧村",
    District.LavenderBeds => "薰衣草苗圃",
    District.TheGoblet => "高腳孤丘",
    District.Shirogane => "白銀鄉",
    District.Empyreum => "穹頂皓天",
    _ => "未知住宅區",
  };

  public static string PlotSizeName(PlotSize? size) => size switch
  {
    PlotSize.Small => "小型",
    PlotSize.Medium => "中型",
    PlotSize.Large => "大型",
    _ => "未知",
  };

  public static string FooleryName(Foolery foolery) => foolery switch
  {
    Foolery.AprilFools => "愚人節",
    Foolery.Christmas => "平安夜",
    _ => "一般日期",
  };
}
