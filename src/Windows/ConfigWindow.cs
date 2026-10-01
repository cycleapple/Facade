namespace Facade.Windows;

public class ConfigWindow(ILogger _logger, Configuration _configuration, IExteriorService _exteriorService, IPlotService _plotService, IDataManager _dataManager, IDalamudPluginInterface _pluginInterface, ITextureProvider _textureProvider) : Window("Facade##FacadeConfigWindow"), IHostedService
{
  private bool _addingFacade = false;
  private Facade? _editingFacade = null;
  private sbyte? _selectedPlot = null;
  private UInt128 _packedSelectedExterior = UInt128Extensions.Pack(null, null, null, null, null, null, null, null);
  private UInt128 _packedSelectedStain = UInt128Extensions.Pack(null, null, null, null, null, null, null, null);
  private bool _unitedExteriorSelection = true;
  private bool _festivalView = false;
  private OverlayContent _overlayContent = OverlayContent.FacadeLocations;
  private string _presetName = "";
  private bool _isShiftDown => ImGui.IsKeyDown(ImGuiKey.ModShift);
  private bool _isCtrlDown => ImGui.IsKeyDown(ImGuiKey.ModCtrl);
  private DateTime _lastExport = new();
  private DateTime _lastFailedImport = new();
  private DateTime _lastImport = new();
  private List<Facade> _importingFacades = [];

  public bool SettingsOpen = false;
  public bool OverlayOpen = false;

  // Took the most recent events as the older ones don't have models for newer districts.
  private readonly List<Festival> _festivals = [
    new Festival { Id = ushort.MaxValue, Name = "不覆寫" },
    new Festival { Id = 00, Name = "無" },
    new Festival { Id = 129, Name = "降神祭" },
    new Festival { Id = 154, Name = "情人節" },
    new Festival { Id = 155, Name = "公主節" },
    new Festival { Id = 156, Name = "獵蛋節" },
    new Festival { Id = 161, Name = "金碟遊樂場嘉年華" },
    new Festival { Id = 159, Name = "紅蓮節" },
    new Festival { Id = 151, Name = "新生慶典" },
    new Festival { Id = 164, Name = "守護天節" },
    new Festival { Id = 165, Name = "星芒節" },
    // Limited to Mist, Goblet, Lavender Beds
    new Festival { Id = 43, Name = "星芒節（無雪）" },
  ];

  private List<ExteriorItem> _cachedExteriorItems = [];

  private List<ExteriorItem> ExteriorItems
  {
    get
    {
      if (_cachedExteriorItems.Count > 0) return _cachedExteriorItems;

      IEnumerable<Item> items = _dataManager.GetExcelSheet<Item>().Where(item => item.AdditionalData.RowId != 0 && item.ItemSearchCategory.RowId == 65);
      foreach (Item item in items)
      {
        if (_dataManager.GetExcelSheet<HousingPreset>().TryGetRow(item.AdditionalData.RowId, out HousingPreset housingPreset))
        {
          if (housingPreset.Singular.IsEmpty) continue;
          if (!_dataManager.GetExcelSheet<Item>().TryGetRow(housingPreset.ExteriorRoof.RowId, out Item roofItem)) continue;
          if (!_dataManager.GetExcelSheet<Item>().TryGetRow(housingPreset.ExteriorWall.RowId, out Item wallItem)) continue;
          if (!_dataManager.GetExcelSheet<Item>().TryGetRow(housingPreset.ExteriorWindow.RowId, out Item windowItem)) continue;
          if (!_dataManager.GetExcelSheet<Item>().TryGetRow(housingPreset.ExteriorDoor.RowId, out Item doorItem)) continue;

          UInt128 packedId = UInt128Extensions.Pack(
            (ushort)roofItem.AdditionalData.RowId,
            (ushort)wallItem.AdditionalData.RowId,
            (ushort)windowItem.AdditionalData.RowId,
            (ushort)doorItem.AdditionalData.RowId,
            0,
            0,
            0,
            0
          );

          string name = housingPreset.Singular.ToString();
          _cachedExteriorItems.Add(new()
          {
            Size = (PlotSize)housingPreset.HousingSize,
            Type = ExteriorItemType.UnitedExteriorPreset,
            Id = packedId,
            Name = name,
            Icon = roofItem.Icon,
            UnitedExteriorId = null
          });
        }
        else if (_dataManager.GetExcelSheet<HousingExterior>().TryGetRow(item.AdditionalData.RowId, out HousingExterior housingExterior))
        {
          _cachedExteriorItems.Add(new()
          {
            Size = housingExterior.HousingSize == 254 ? null : (PlotSize)housingExterior.HousingSize,
            Type = (ExteriorItemType)housingExterior.Unknown1 - 1,
            Id = housingExterior.RowId,
            Name = item.Name.ToString(),
            Icon = item.Icon,
            UnitedExteriorId = null
          });
        }
        else if (_dataManager.GetExcelSheet<HousingUnitedExterior>().TryGetRow(item.AdditionalData.RowId, out HousingUnitedExterior housingUnitedExterior))
        {
          UInt128 packedId = UInt128Extensions.Pack(
            (ushort)housingUnitedExterior.Roof.RowId,
            (ushort)housingUnitedExterior.Walls.RowId,
            (ushort)housingUnitedExterior.Windows.RowId,
            (ushort)housingUnitedExterior.Door.RowId,
            (ushort)housingUnitedExterior.OptionalRoof.RowId,
            (ushort)housingUnitedExterior.OptionalWall.RowId,
            (ushort)housingUnitedExterior.OptionalSignboard.RowId,
            (ushort)housingUnitedExterior.Fence.RowId
          );

          _cachedExteriorItems.Add(new()
          {
            Size = (PlotSize)housingUnitedExterior.PlotSize,
            Type = ExteriorItemType.UnitedExterior,
            Id = packedId,
            Name = item.Name.ToString(),
            Icon = item.Icon,
            UnitedExteriorId = housingUnitedExterior.RowId
          });
        }
      }

      _cachedExteriorItems = _cachedExteriorItems.OrderByDescending(item => item.Type == ExteriorItemType.UnitedExteriorPreset).ToList();

      return _cachedExteriorItems;
    }
  }

  private readonly IFontHandle _uiFont = _pluginInterface.UiBuilder.FontAtlas.NewDelegateFontHandle(e =>
  {
    e.OnPreBuild(tk =>
    {
      float fontPx = UiBuilder.DefaultFontSizePx;
      SafeFontConfig safeFontConfig = new() { SizePx = fontPx, GlyphRanges = [0x0020, 0x00FF, 0x2000, 0x206F, 0x3000, 0x30FF, 0x3400, 0x9FFF, 0xFF00, 0xFFEF, 0] };
      tk.AddDalamudAssetFont(Dalamud.DalamudAsset.NotoSansScMedium, safeFontConfig);
      tk.AttachExtraGlyphsForDalamudLanguage(safeFontConfig);
    });
  });

  public Task StartAsync(CancellationToken cancellationToken)
  {
    _exteriorService.OnDivisionChange += OnDivisionChange;

    Flags = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.AlwaysAutoResize;
    SizeCondition = ImGuiCond.Always;

    _logger.ServiceLifecycle();
    return Task.CompletedTask;
  }

  public Task StopAsync(CancellationToken cancellationToken)
  {
    _exteriorService.OnDivisionChange -= OnDivisionChange;
    _uiFont.Dispose();

    _logger.ServiceLifecycle();
    return Task.CompletedTask;
  }

  private void OnDivisionChange(object? sender, object _)
  {
    OverlayOpen = false;
    _editingFacade = null;
    _addingFacade = false;
  }

  private float ScaledFloat(float value) => value * ImGuiHelpers.GlobalScale;
  private Vector2 ScaledVector2(float x, float? y = null) => new Vector2(x, y ?? x) * ImGuiHelpers.GlobalScale;

  private string SerializeToBase64(object obj)
  {
    string json = Newtonsoft.Json.JsonConvert.SerializeObject(obj);
    byte[] bytes = Encoding.UTF8.GetBytes(json);
    using MemoryStream compressedStream = new();
    using (GZipStream zipStream = new(compressedStream, CompressionMode.Compress))
      zipStream.Write(bytes, 0, bytes.Length);
    return Convert.ToBase64String(compressedStream.ToArray());
  }

  private T? DeserializeFromBase64<T>(string base64)
  {
    try
    {
      byte[] bytes = Convert.FromBase64String(base64);
      using MemoryStream compressedStream = new(bytes);
      using GZipStream zipStream = new(compressedStream, CompressionMode.Decompress);
      using MemoryStream resultStream = new();
      zipStream.CopyTo(resultStream);
      bytes = resultStream.ToArray();
      string json = Encoding.UTF8.GetString(bytes);
      T? deserializedObject = Newtonsoft.Json.JsonConvert.DeserializeObject<T>(json);
      if (deserializedObject is T typedObject)
      {
        return typedObject;
      }
    }
    catch { }

    return default;
  }

  public override void Draw()
  {
    TitleBarButtons = [
      new()
      {
        Icon = SettingsOpen ? FontAwesomeIcon.Home : FontAwesomeIcon.Cog,
        ShowTooltip = () => ImGui.SetTooltip(SettingsOpen ? "外觀設定" : "設定"),
        Click = (btn) => SettingsOpen = !SettingsOpen,
      },
    ];

    SizeConstraints = new()
    {
      MinimumSize = ScaledVector2(380, SettingsOpen ? 0 : 300),
      MaximumSize = ScaledVector2(380, (_addingFacade || _editingFacade != null) && !_unitedExteriorSelection ? 999 : 300)
    };

    using IDisposable _ = _uiFont.Push();

    if (DrawSettings()) return;

    if (DrawEditScreen())
    {
      DrawOverlay();
      return;
    }

    bool buttonsDisabled = _plotService.CurrentDivision == 0 || _exteriorService.ActiveFoolery != Foolery.None;
    bool buttonsDisabledFestivalView = buttonsDisabled || _festivalView;
    bool buttonsHovered = false;
    using (ImRaii.PushStyle(ImGuiStyleVar.Alpha, ImGui.GetStyle().Alpha * 0.5f, buttonsDisabled))
    {
      using (ImRaii.PushStyle(ImGuiStyleVar.Alpha, ImGui.GetStyle().Alpha * (buttonsDisabled ? 1 : 0.5f), buttonsDisabledFestivalView))
      {
        if (ImGui.Button("新增外觀", new(ImGui.GetContentRegionAvail().X - ScaledFloat(35 * 2) - (ImGui.GetStyle().ItemSpacing.X * 2), ScaledFloat(35))))
        {
          if (!buttonsDisabled && !buttonsDisabledFestivalView)
          {
            _selectedPlot = null;
            _packedSelectedExterior = UInt128Extensions.Pack(null, null, null, null, null, null, null, null);
            _packedSelectedStain = UInt128Extensions.Pack(null, null, null, null, null, null, null, null);
            _unitedExteriorSelection = true;
            _addingFacade = true;
          }
        }
        if (!buttonsHovered) buttonsHovered = ImGui.IsItemHovered();

        ImGui.SameLine();
        bool failedImport = (DateTime.Now - _lastFailedImport).TotalSeconds <= 3;
        bool successfulImport = (DateTime.Now - _lastImport).TotalSeconds <= 3;
        bool successfulExport = (DateTime.Now - _lastExport).TotalSeconds <= 3;
        if (ImGuiComponents.IconButton("##ImportExportButton", failedImport ? FontAwesomeIcon.Times : successfulExport || successfulImport ? FontAwesomeIcon.Check : _isShiftDown ? FontAwesomeIcon.FileExport : FontAwesomeIcon.FileImport, new(35)))
        {
          if (!buttonsDisabled && !buttonsDisabledFestivalView && !failedImport && !successfulImport && !successfulExport)
          {
            if (_isShiftDown)
            {
              ImGui.SetClipboardText(SerializeToBase64(_exteriorService.GetCurrentFacades()));
              _lastExport = DateTime.Now;
            }
            else
            {
              List<Facade>? facades = DeserializeFromBase64<List<Facade>>(ImGui.GetClipboardText());
              if (facades == null || facades.Count == 0)
              {
                _lastFailedImport = DateTime.Now;
              }
              else
              {
                _importingFacades = facades;
                _overlayContent = OverlayContent.FacadeImport;
                OverlayOpen = true;
              }
            }
          }
        }
        if (!buttonsHovered) buttonsHovered = ImGui.IsItemHovered();

        if (ImGui.IsItemHovered() && !buttonsDisabled && !buttonsDisabledFestivalView)
        {
          using (ImRaii.Tooltip())
          {
            ImGui.Text(failedImport ? "剪貼簿中沒有有效資料" : successfulImport ? $"已匯入 {_importingFacades.Count} 筆外觀！" : successfulExport ? "已複製到剪貼簿！" : _isShiftDown ? "將此分區的外觀匯出至剪貼簿" : "從剪貼簿匯入外觀\n（按住 SHIFT 可匯出）");
          }
        }
      }

      ImGui.SameLine();
      if (ImGuiComponents.IconButton("##SwitchButton", _festivalView ? FontAwesomeIcon.Snowflake : FontAwesomeIcon.HouseChimney, new(35)))
      {
        if (!buttonsDisabled)
        {
          _festivalView = !_festivalView;
        }
      }
      if (!buttonsHovered) buttonsHovered = ImGui.IsItemHovered();

      if (ImGui.IsItemHovered() && !buttonsDisabled)
      {
        using (ImRaii.Tooltip())
        {
          ImGui.Text(_festivalView ? "切換至住宅外觀" : "切換至節慶裝飾");
        }
      }
    }

    if (buttonsDisabled)
    {
      if (buttonsHovered || ImGui.IsItemHovered())
      {
        using (ImRaii.Tooltip())
        {
          ImGui.Text(_exteriorService.ActiveFoolery != Foolery.None ? "由節日彩蛋控制" : "請先前往要修改外觀的住宅分區。");
        }
      }
    }

    using (ImRaii.IEndObject child = ImRaii.Child("##facadeList"))
    {
      if (!child.Success) return;
      if (_festivalView) DrawFestivalFacadeList();
      else DrawFacadeList();
    }

    DrawOverlay();
  }

  private bool DrawSettings()
  {
    if (!SettingsOpen) return false;

    ImGui.TextWrapped("其他玩家進入已修改外觀的地皮時：");
    ImGui.Dummy(ScaledVector2(0, 5));

    if (ImGui.RadioButton("不處理", _configuration.PlayerBehavior == PlayerBehavior.Nothing))
    {
      _configuration.PlayerBehavior = PlayerBehavior.Nothing;
      _configuration.Save();
    }

    if (ImGui.IsItemHovered())
      using (ImRaii.Tooltip())
        ImGui.Text("玩家可能穿過建築物或浮在空中。");

    if (ImGui.RadioButton("還原外觀", _configuration.PlayerBehavior == PlayerBehavior.ResetFacade))
    {
      _configuration.PlayerBehavior = PlayerBehavior.ResetFacade;
      _configuration.Save();
    }

    if (ImGui.IsItemHovered())
      using (ImRaii.Tooltip())
        ImGui.Text("玩家進入地皮時，暫時還原該地皮的外觀。");

    if (ImGui.RadioButton("隱藏玩家", _configuration.PlayerBehavior == PlayerBehavior.HidePlayers))
    {
      _configuration.PlayerBehavior = PlayerBehavior.HidePlayers;
      _configuration.Save();
    }

    if (ImGui.IsItemHovered())
      using (ImRaii.Tooltip())
        ImGui.Text("隱藏位於已修改外觀地皮上的其他玩家。");

    ImGui.Dummy(ScaledVector2(0, 5));

    if (!_configuration.Foolery || _exteriorService.ActiveFoolery != Foolery.None)
    {
      bool foolery = _configuration.Foolery;
      if (ImGui.Checkbox($"節日彩蛋", ref foolery))
      {
        _configuration.Foolery = foolery;
        _configuration.Save();
      }

      if (ImGui.IsItemHovered())
        using (ImRaii.Tooltip())
          ImGui.Text("在特定日期自動套用特殊外觀。");
    }

    return true;
  }

  private uint ABGRtoARGB(uint color) => ((color & 0xFF) << 16) | ((color >> 16) & 0xFF) | (color & 0xFF00) | 0xFF000000;

  private void DrawCenteredText(string text, bool horizontal = false, bool vertical = false)
  {
    float textHeight = ImGui.CalcTextSize(text).Y;
    if (vertical) ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (textHeight * 0.6f));

    float textWidth = ImGui.CalcTextSize(text).X;
    float textX = (ImGui.GetContentRegionAvail().X - textWidth) * 0.5f;
    if (horizontal) ImGui.SetCursorPosX(ImGui.GetCursorPosX() + textX);

    ImGui.Text(text);
  }

  private string DrawColorCircle(uint? stain, uint unscaledSize, uint unscaledPaddingTopLeft = 0)
  {
    string stainName = "原有顏色";
    uint stainColor = ColorHelpers.RgbaVector4ToUint(ImGui.GetStyle().Colors[(int)ImGuiCol.WindowBg]);
    if (stain != null && _dataManager.GetExcelSheet<Stain>().TryGetRow((uint)stain, out Stain stainRow))
    {
      stainName = stainRow.RowId == 0 ? "未染色" : stainRow.Name.ToString();
      stainColor = ABGRtoARGB(stainRow.Color);
    }
    Vector2 pos = ImGui.GetCursorScreenPos() + ScaledVector2(unscaledPaddingTopLeft);
    ImGui.GetWindowDrawList().AddRectFilled(pos - ScaledVector2(1), pos + ScaledVector2(unscaledSize + 1), 0xFFFFFFFF, ScaledFloat((unscaledSize + 2) / 2));
    ImGui.GetWindowDrawList().AddRectFilled(pos, pos + ScaledVector2(unscaledSize), stainColor, ScaledFloat(unscaledSize / 2));
    return stainName;
  }

  private void DrawFestivalFacadeList()
  {
    using (ImRaii.Disabled(_plotService.CurrentDivision == 0))
    {
      ImGui.Dummy(ScaledVector2(4));

      FestivalFacade? currentFestivalFacade = _exteriorService.GetCurrentFestivalFacade();
      ushort currentFestivalId = currentFestivalFacade == null ? ushort.MaxValue : currentFestivalFacade.Id;
      Festival currentFestival = _festivals.Find(festival => festival.Id == currentFestivalId);

      ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
      using (ImRaii.IEndObject combo = ImRaii.Combo("###festivalCombo", currentFestival.Name))
      {
        if (combo.Success)
        {
          foreach (Festival festival in _festivals)
          {
            if (festival.Id == 43 && _plotService.CurrentDistrict != District.Mist && _plotService.CurrentDistrict != District.TheGoblet && _plotService.CurrentDistrict != District.LavenderBeds) continue;
            if (ImGui.Selectable(festival.Name, currentFestivalId == festival.Id))
            {
              if (currentFestivalFacade != null && festival.Id == ushort.MaxValue)
              {
                _configuration.FestivalFacades.Remove(currentFestivalFacade);
              }
              else if (currentFestivalFacade != null && festival.Id != ushort.MaxValue)
              {
                currentFestivalFacade.Id = festival.Id;
              }
              else if (currentFestivalFacade == null)
              {
                _configuration.FestivalFacades.Add(new FestivalFacade()
                {
                  World = _plotService.CurrentWorld,
                  District = _plotService.CurrentDistrict,
                  Ward = _plotService.CurrentWard,
                  Id = festival.Id,
                });
              }

              _configuration.Save();
              _exteriorService.UpdateFestival();
            }
          }
        }
      }

      int otherFacadeCount = _configuration.FestivalFacades.Count - (currentFestivalFacade == null ? 0 : 1);
      DrawCenteredText($"其他區共有 {otherFacadeCount} 筆節慶裝飾設定。", true, false);
      if (ImGui.IsItemHovered())
      {
        using (ImRaii.Tooltip())
        {
          ImGui.Text("只能變更目前所在區的節慶裝飾。\n（點擊查看其他節慶設定的位置。）");
        }
      }
      if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
      {
        _overlayContent = OverlayContent.FacadeLocations;
        OverlayOpen = true;
      }
    }

    using (ImRaii.Disabled())
    {
      ImGui.Dummy(ScaledVector2(8));
      ImGui.TextWrapped("改回「不覆寫」時，原本的節慶裝飾可能不會立即恢復。請離開並重新進入此區。");
    }
  }

  private void DrawFacadeList()
  {
    IEnumerable<Facade> facades = _exteriorService.GetCurrentFacades();

    ImGui.Dummy(ScaledVector2(1));

    if (_exteriorService.ActiveFoolery != Foolery.None)
    {
      DrawCenteredText("外觀目前由節日彩蛋控制。", true, false);
      if (ImGui.IsItemHovered())
        using (ImRaii.Tooltip())
          ImGui.Text($"今天是 {ZhTw.FooleryName(_exteriorService.ActiveFoolery)}。可在設定中關閉節日彩蛋。");
      return;
    }
    else if (facades.Count() == 0)
    {
      DrawCenteredText("目前分區尚未設定任何外觀。", true, false);
    }
    else
    {
      using (ImRaii.PushColor(ImGuiCol.TableBorderStrong, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]))
      using (ImRaii.PushColor(ImGuiCol.TableBorderLight, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]))
      using (ImRaii.IEndObject table = ImRaii.Table("facadeTable", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
      {
        if (!table.Success) return;

        ImGui.TableSetupColumn("##icon", ImGuiTableColumnFlags.WidthFixed, ScaledFloat(40));
        ImGui.TableSetupColumn("##stain", ImGuiTableColumnFlags.WidthFixed, ScaledFloat(40));
        ImGui.TableSetupColumn("##plot", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("##action", ImGuiTableColumnFlags.WidthFixed, ScaledFloat(40));

        foreach (Facade facade in facades)
        {
          ImGui.TableNextRow();
          ImGui.TableNextColumn();
          (ISharedImmediateTexture? icon, string iconDescription) = GetExteriorIcon(facade.Plot, facade);
          if (icon == null) continue;
          ImGui.Image(icon.GetWrapOrEmpty().Handle, ScaledVector2(40));

          if (ImGui.IsItemHovered())
          {
            using (ImRaii.Tooltip())
            {
              ImGui.Text(iconDescription);
            }
          }

          ImGui.TableNextColumn();
          string stainName = DrawColorCircle(facade.PackedStainIds.Unpack().u1, 28, 6);
          ImGui.Dummy(ScaledVector2(40));

          if (ImGui.IsItemHovered())
          {
            using (ImRaii.Tooltip())
            {
              ImGui.Text(stainName);
            }
          }

          ImGui.TableNextColumn();
          PlotSize? plotSize = _exteriorService.GetPlotSize(facade.Plot);
          DrawCenteredText($"{facade.Plot + 1} 號地（{ZhTw.PlotSizeName(plotSize)}）", true, true);

          ImGui.TableNextColumn();
          if (ImGuiComponents.IconButton($"##Edit{facade.Plot}", FontAwesomeIcon.Edit, new(40)))
          {
            _editingFacade = facade;
            _unitedExteriorSelection = _editingFacade.IsUnitedExterior;
            _selectedPlot = (sbyte)(_editingFacade.Plot + 1);
            _packedSelectedExterior = _editingFacade.PackedExteriorIds;
            _packedSelectedStain = _editingFacade.PackedStainIds;
          }
        }
      }
    }

    int otherFacadeCount = _configuration.Facades.Count - facades.Count();
    DrawCenteredText($"其他分區另有 {otherFacadeCount} 筆外觀設定。", true, false);
    if (ImGui.IsItemHovered())
    {
      using (ImRaii.Tooltip())
      {
        ImGui.Text("此處只顯示目前分區的外觀。\n（點擊查看其他外觀設定的位置。）");
      }
    }
    if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
    {
      _overlayContent = OverlayContent.FacadeLocations;
      OverlayOpen = true;
    }
  }

  private void DrawOverlay()
  {
    if (!OverlayOpen) return;

    ImGui.SetCursorPos(new(0, 0));

    Vector2 windowSize = ImGui.GetContentRegionAvail();
    Vector4 overlayColor = new(0f, 0f, 0f, 0.75f);
    Vector4 childColor = ImGui.GetStyle().Colors[(int)ImGuiCol.WindowBg];

    using (ImRaii.PushColor(ImGuiCol.ChildBg, overlayColor))
    {
      using (ImRaii.IEndObject overlay = ImRaii.Child("##overlay", windowSize, false))
      {
        if (!overlay.Success) return;

        Vector2 childSize = ScaledVector2(230, _overlayContent == OverlayContent.FacadeLocations ? 230 : _overlayContent == OverlayContent.FacadeImport ? 140 : _overlayContent == OverlayContent.SaveNewPreset ? 90 : 400);
        childSize.Y -= windowSize.Y * 0.04f;
        Vector2 childPos = (windowSize - childSize) / 2.0f;
        childPos.Y += windowSize.Y * 0.04f;
        ImGui.SetCursorPos(new(childPos.X, childPos.Y));

        using (ImRaii.PushColor(ImGuiCol.ChildBg, childColor))
        {
          using (ImRaii.IEndObject child = ImRaii.Child("##facadeLocation", childSize, false, ImGuiWindowFlags.AlwaysUseWindowPadding))
          {
            if (!child.Success) return;
            if (_overlayContent == OverlayContent.FacadeLocations) DrawFacadeLocations();
            else if (_overlayContent == OverlayContent.FacadeImport) DrawFacadeImport();
            else DrawPresets();
          }
        }
      }

      if (ImGui.IsItemClicked())
      {
        OverlayOpen = false;
      }
    }
  }

  private void DrawFacadeImport()
  {
    if (_importingFacades.Count == 0) return;
    Facade probedFacade = _importingFacades[0];
    if (!_dataManager.GetExcelSheet<World>().TryGetRow(probedFacade.World, out World world)) return;

    int division = probedFacade.Plot >= 30 ? 2 : 1;
    ImGui.Text("匯入外觀至：");
    ImGui.TextWrapped($"（{world.Name}－{ZhTw.DistrictName(probedFacade.District)}－第 {probedFacade.Ward + 1} 區－{(division == 1 ? "一般區" : "擴建區")}）");
    ImGui.TextWrapped("這可能覆寫該分區中已有的外觀設定。");

    if (ImGui.Button("確認", new(ImGui.GetContentRegionAvail().X / 2, ScaledFloat(30))))
    {
      foreach (Facade facade in _importingFacades)
      {
        Facade? existingFacade = _configuration.Facades.Find(f => f.World == facade.World && f.District == facade.District && f.Ward == facade.Ward && f.Plot == facade.Plot);
        if (existingFacade != null)
        {
          _configuration.Facades.Remove(existingFacade);
        }

        _configuration.Facades.Add(facade);
      }
      _configuration.Save();
      _exteriorService.UpdateExteriors();
      _lastImport = DateTime.Now;
      OverlayOpen = false;
    }

    ImGui.SameLine();
    if (ImGui.Button("取消", new(ImGui.GetContentRegionAvail().X, ScaledFloat(30))))
    {
      OverlayOpen = false;
    }
  }

  private void DrawPresets()
  {
    PlotSize? plotSize = _exteriorService.GetPlotSize((sbyte)((_selectedPlot ?? 0) - 1));
    if (plotSize == null)
    {
      ImGui.Text("發生錯誤，請關閉視窗後重試。");
      return;
    }

    if (_overlayContent == OverlayContent.SaveNewPreset)
    {
      ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
      ImGui.InputTextWithHint("##presetName", "預設名稱", ref _presetName);

      bool nameTaken = _configuration.Presets.Where(p => p.Name == _presetName).Count() > 0;
      using (ImRaii.Disabled(_presetName.Length == 0 || nameTaken))
      {
        if (ImGui.Button("儲存", new(ImGui.GetContentRegionAvail().X / 2, ScaledFloat(30))))
        {
          _configuration.Presets.Add(new Preset()
          {
            Name = _presetName,
            PlotSize = (PlotSize)plotSize,
            PackedExteriorIds = _packedSelectedExterior,
            PackedStainIds = _packedSelectedStain,
          });
          _configuration.Save();
          OverlayOpen = false;
        }
      }
      ImGui.SameLine();
      if (ImGui.Button("取消", new(ImGui.GetContentRegionAvail().X, ScaledFloat(30))))
      {
        _overlayContent = OverlayContent.SavePreset;
      }
      return;
    }

    using (ImRaii.Disabled(_overlayContent != OverlayContent.SavePreset))
    {
      if (ImGui.Button("新增預設", new(ImGui.GetContentRegionAvail().X - ScaledFloat(30) - ImGui.GetStyle().ItemSpacing.X, ScaledFloat(30))))
      {
        _presetName = "";
        _overlayContent = OverlayContent.SaveNewPreset;
      }
    }

    ImGui.SameLine();
    bool failedImport = (DateTime.Now - _lastFailedImport).TotalSeconds <= 3;
    bool successfulImport = (DateTime.Now - _lastImport).TotalSeconds <= 3;
    if (ImGuiComponents.IconButton("##importPresetButton", failedImport ? FontAwesomeIcon.Times : successfulImport ? FontAwesomeIcon.Check : FontAwesomeIcon.FileImport, new(30)))
    {
      if (!failedImport && !successfulImport)
      {
        Preset? preset = DeserializeFromBase64<Preset>(ImGui.GetClipboardText());
        if (preset == null)
        {
          _lastFailedImport = DateTime.Now;
        }
        else
        {
          if (_configuration.Presets.Find(p => p.Name == preset.Name) != null)
          {
            preset.Name = $"{preset.Name} (2)";
          }
          _configuration.Presets.Add(preset);
          _configuration.Save();
          _lastImport = DateTime.Now;
        }
      }
    }

    if (ImGui.IsItemHovered())
    {
      using (ImRaii.Tooltip())
      {
        ImGui.Text(failedImport ? "剪貼簿中沒有有效資料" : successfulImport ? "已匯入預設！" : "匯入預設");
      }
    }

    ImGui.Dummy(ScaledVector2(2));

    if (_configuration.Presets.Count == 0)
    {
      DrawCenteredText("尚無預設", true, false);
      return;
    }

    bool exporting = _isShiftDown;
    bool deleting = _isCtrlDown;

    using (ImRaii.PushColor(ImGuiCol.TableBorderStrong, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]))
    using (ImRaii.PushColor(ImGuiCol.TableBorderLight, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]))
    using (ImRaii.IEndObject table = ImRaii.Table("##presetTable", 2, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
    {
      if (!table.Success) return;

      ImGui.TableSetupColumn("名稱", ImGuiTableColumnFlags.WidthStretch);
      ImGui.TableSetupColumn("##action", ImGuiTableColumnFlags.WidthFixed, ScaledFloat(40));

      foreach (Preset preset in _configuration.Presets)
      {
        using (ImRaii.Disabled(plotSize != preset.PlotSize && !deleting && !exporting))
        {
          ImGui.TableNextRow();
          ImGui.TableNextColumn();
          DrawCenteredText($"（{ZhTw.PlotSizeName(preset.PlotSize)}）{preset.Name}", true, true);

          ImGui.TableNextColumn();
          bool successfulExport = (DateTime.Now - _lastExport).TotalSeconds <= 3;
          FontAwesomeIcon icon = successfulExport ? FontAwesomeIcon.Check : exporting ? FontAwesomeIcon.FileExport : deleting ? FontAwesomeIcon.Trash : _overlayContent == OverlayContent.SavePreset ? FontAwesomeIcon.Save : FontAwesomeIcon.Check;
          if (ImGuiComponents.IconButton($"##Action{preset.Name}", icon, new(40)))
          {
            if (!successfulExport)
            {
              if (deleting)
              {
                _configuration.Presets.Remove(preset);
                _configuration.Save();
                return;
              }
              else if (exporting)
              {
                ImGui.SetClipboardText(SerializeToBase64(preset));
                _lastExport = DateTime.Now;
              }
              else if (_overlayContent == OverlayContent.SavePreset)
              {
                preset.PackedExteriorIds = _packedSelectedExterior;
                preset.PackedStainIds = _packedSelectedStain;
                _configuration.Save();
                OverlayOpen = false;
              }
              else if (_overlayContent == OverlayContent.LoadPreset)
              {
                _packedSelectedExterior = preset.PackedExteriorIds;
                _packedSelectedStain = preset.PackedStainIds;
                OverlayOpen = false;
              }
            }
          }

          if (ImGui.IsItemHovered())
          {
            using (ImRaii.Tooltip())
            {
              ImGui.Text(successfulExport ? "已複製到剪貼簿！" : exporting ? "將預設匯出至剪貼簿" : deleting ? "刪除預設" : _overlayContent == OverlayContent.SavePreset ? "覆寫此預設" : "載入此預設");
            }
          }
        }
      }
    }

    ImGui.Dummy(ScaledVector2(2));
    DrawCenteredText("將滑鼠移至此處查看說明。", true, false);
    if (ImGui.IsItemHovered())
    {
      using (ImRaii.Tooltip())
      {
        ImGui.Text("只能載入符合地皮大小的預設。\n按住 SHIFT 可匯出預設。\n按住 CTRL 可刪除預設。");
      }
    }
  }

  private void DrawFacadeLocations()
  {
    using (ImRaii.PushColor(ImGuiCol.TableBorderStrong, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]))
    using (ImRaii.PushColor(ImGuiCol.TableBorderLight, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]))
    using (ImRaii.IEndObject table = ImRaii.Table("##facadeLocationsTable", _festivalView ? 3 : 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
    {
      if (!table.Success) return;

      ImGui.TableSetupColumn("伺服器", ImGuiTableColumnFlags.WidthStretch);
      ImGui.TableSetupColumn("住宅區", ImGuiTableColumnFlags.WidthStretch);
      ImGui.TableSetupColumn("區", ImGuiTableColumnFlags.WidthFixed, ScaledFloat(30));
      if (!_festivalView) ImGui.TableSetupColumn("總數", ImGuiTableColumnFlags.WidthFixed, ScaledFloat(30));
      ImGui.TableHeadersRow();

      if (_festivalView)
      {
        IEnumerable<FestivalFacade> festivalFacades = _configuration.FestivalFacades.Where(f => !(f.World == _plotService.CurrentWorld && f.District == _plotService.CurrentDistrict && f.Ward == _plotService.CurrentWard)).ToList();

        foreach (FestivalFacade festivalFacade in festivalFacades)
        {
          if (!_dataManager.GetExcelSheet<World>().TryGetRow(festivalFacade.World, out World world)) continue;

          ImGui.TableNextRow();
          ImGui.TableNextColumn();
          ImGui.Text(world.Name.ToString());

          ImGui.TableNextColumn();
          ImGui.Text(ZhTw.DistrictName(festivalFacade.District));

          ImGui.TableNextColumn();
          ImGui.Text((festivalFacade.Ward + 1).ToString());
        }
      }
      else
      {
        var facades = _configuration.Facades
          .Where(f => !(f.World == _plotService.CurrentWorld && f.District == _plotService.CurrentDistrict && f.Ward == _plotService.CurrentWard))
          .GroupBy(f => new { f.World, f.District, f.Ward })
          .Select(g => new { Facade = g.First(), Count = g.Count() }).ToList();

        foreach (var group in facades)
        {
          Facade facade = group.Facade;
          if (!_dataManager.GetExcelSheet<World>().TryGetRow(facade.World, out World world)) continue;

          ImGui.TableNextRow();
          ImGui.TableNextColumn();
          ImGui.Text(world.Name.ToString());

          ImGui.TableNextColumn();
          ImGui.Text(ZhTw.DistrictName(facade.District));

          ImGui.TableNextColumn();
          ImGui.Text((facade.Ward + 1).ToString());

          ImGui.TableNextColumn();
          ImGui.Text(group.Count.ToString());
        }
      }
    }
  }

  private (ISharedImmediateTexture? icon, string description) GetExteriorIcon(sbyte plot, Facade? facade)
  {
    PlotSize? plotSize = _exteriorService.GetPlotSize(plot);
    uint defaultIconId = 60751 + (uint)(plotSize ?? 0);
    uint individualIconId = 60761 + (uint)(plotSize ?? 0);
    uint unitedIconId = defaultIconId;
    string description = "原有外觀";
    if (facade != null)
    {
      ExteriorItem? exteriorItem = ExteriorItems.FirstOrNull(item => item.Id == facade.PackedExteriorIds);

      if (exteriorItem != null && exteriorItem.HasValue && exteriorItem.Value.UnitedExteriorId != null)
      {
        unitedIconId = (uint)exteriorItem.Value.UnitedExteriorId - 276880;
        description = exteriorItem.Value.Name;
      }

      if (exteriorItem != null && exteriorItem.HasValue && exteriorItem.Value.Type == ExteriorItemType.UnitedExteriorPreset)
      {
        unitedIconId = exteriorItem.Value.Icon;
        description = exteriorItem.Value.Name;
      }

      if (!facade.IsUnitedExterior) description = "自訂組合外觀";
    }
    uint iconId = facade == null ? defaultIconId : facade.IsUnitedExterior ? unitedIconId : individualIconId;
    if (!_textureProvider.TryGetFromGameIcon(new GameIconLookup(iconId), out ISharedImmediateTexture? icon)) return (null, "未知");
    return (icon, description);
  }

  private bool DrawEditScreen()
  {
    if (_editingFacade == null && !_addingFacade) return false;

    if (_addingFacade) DrawCenteredText("新增外觀", true, false);
    if (_editingFacade != null) DrawCenteredText($"編輯外觀（{_selectedPlot} 號地）", true, false);

    if (_addingFacade) DrawPlotDropdown();

    using (ImRaii.Disabled(_selectedPlot == null))
    {
      ImGui.Dummy(ScaledVector2(60, 0));
      ImGui.SameLine();
      if (ImGui.RadioButton("整套外觀", _unitedExteriorSelection))
        _unitedExteriorSelection = true;
      ImGui.SameLine();
      if (ImGui.RadioButton("個別部件", !_unitedExteriorSelection))
        _unitedExteriorSelection = false;
    }

    if (!_unitedExteriorSelection)
    {
      ImGui.Dummy(ScaledVector2(4));

      if (ImGui.Button("載入預設", new(ImGui.GetContentRegionAvail().X / 2, ScaledFloat(30))))
      {
        _overlayContent = OverlayContent.LoadPreset;
        OverlayOpen = true;
      }

      ImGui.SameLine();
      if (ImGui.Button("儲存預設", new(ImGui.GetContentRegionAvail().X, ScaledFloat(30))))
      {
        _overlayContent = OverlayContent.SavePreset;
        OverlayOpen = true;
      }

      ImGui.Dummy(ScaledVector2(4));
    }

    bool invalidSelection = DrawExteriorDropdown();
    DrawStainDropdown();

    ImGui.Dummy(ScaledVector2(4));

    if (_addingFacade)
    {
      using (ImRaii.PushStyle(ImGuiStyleVar.Alpha, ImGui.GetStyle().Alpha * 0.5f, _selectedPlot == null || invalidSelection))
      {
        if (ImGui.Button("確認", new(ImGui.GetContentRegionAvail().X / 2, ScaledFloat(30))))
        {
          if (_selectedPlot != null && !invalidSelection)
          {
            _configuration.Facades.Add(new Facade
            {
              World = _plotService.CurrentWorld,
              District = _plotService.CurrentDistrict,
              Ward = _plotService.CurrentWard,
              Plot = (sbyte)(_selectedPlot! - 1),
              PackedExteriorIds = _packedSelectedExterior,
              PackedStainIds = _packedSelectedStain,
              IsUnitedExterior = _unitedExteriorSelection,
            });
            _configuration.Save();
            _exteriorService.UpdateExteriors();
            _addingFacade = false;
          }
        }
      }

      if (ImGui.IsItemHovered() && invalidSelection)
      {
        using (ImRaii.Tooltip())
        {
          ImGui.Text("請先選擇所有外觀部件");
        }
      }

      ImGui.SameLine();
      if (ImGui.Button("取消", new(ImGui.GetContentRegionAvail().X, ScaledFloat(30))))
      {
        _addingFacade = false;
      }
    }

    if (_editingFacade != null)
    {
      using (ImRaii.PushStyle(ImGuiStyleVar.Alpha, ImGui.GetStyle().Alpha * 0.5f, _selectedPlot == null || invalidSelection))
      {
        if (ImGui.Button("儲存", new(ImGui.GetContentRegionAvail().X / 2, ScaledFloat(30))))
        {
          if (_selectedPlot != null && !invalidSelection)
          {
            // If a United Facade, make sure "原有外觀" option is saved if nothing was manually selected.
            if (_unitedExteriorSelection)
            {
              if (ExteriorItems.Where(item => item.Id == _packedSelectedExterior).Count() == 0)
              {
                _packedSelectedExterior = UInt128Extensions.Pack(null, null, null, null, null, null, null, null);
              }
            }

            // If a United Facade, save stains in all slots since we technically only selected the roof stain.
            if (_unitedExteriorSelection)
            {
              (ushort? n1, ushort? n2, ushort? n3, ushort? n4, ushort? n5, ushort? n6, ushort? n7, ushort? n8) = _packedSelectedStain.Unpack();
              _packedSelectedStain = UInt128Extensions.Pack(n1, n1, n1, n1, n1, n1, n1, n1);
            }

            _editingFacade.Plot = (sbyte)(_selectedPlot! - 1);
            _editingFacade.PackedExteriorIds = _packedSelectedExterior;
            _editingFacade.PackedStainIds = _packedSelectedStain;
            _editingFacade.IsUnitedExterior = _unitedExteriorSelection;
            _configuration.Save();
            _exteriorService.UpdateExteriors();
            _editingFacade = null;
          }
        }
      }

      if (ImGui.IsItemHovered() && invalidSelection)
      {
        using (ImRaii.Tooltip())
        {
          ImGui.Text("請先選擇所有外觀部件");
        }
      }

      ImGui.SameLine();
      if (ImGui.Button("取消", new(ImGui.GetContentRegionAvail().X, ScaledFloat(30))))
      {
        _editingFacade = null;
      }

      if (ImGui.Button("刪除", new(ImGui.GetContentRegionAvail().X, ScaledFloat(30))))
      {
        _configuration.Facades.Remove(_editingFacade!);
        _configuration.Save();
        _exteriorService.UpdateExteriors();
        _editingFacade = null;
      }
    }

    return true;
  }

  private void DrawPlotDropdown()
  {
    IEnumerable<Facade> facades = _exteriorService.GetCurrentFacades();
    ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
    using (ImRaii.IEndObject dropdown = ImRaii.Combo("##PlotSelect", _selectedPlot == null ? "選擇地皮" : _selectedPlot.ToString()))
    {
      if (!dropdown.Success) return;
      for (int i = _plotService.DivisionMin + 1; i < _plotService.DivisionMax + 1; i++)
      {
        if (facades.Any(f => f.Plot == i - 1)) continue;
        if (ImGui.Selectable(i.ToString(), _selectedPlot == i))
        {
          _selectedPlot = (sbyte)i;
        }
      }
    }
  }

  private bool DrawExteriorDropdown()
  {
    bool invalidSelection = false;
    PlotSize? plotSize = _exteriorService.GetPlotSize((sbyte)((_selectedPlot ?? 0) - 1));
    using (ImRaii.Disabled(_selectedPlot == null))
    {
      (ushort? roof, ushort? walls, ushort? windows, ushort? door, ushort? optionalRoof, ushort? optionalWall, ushort? optionalSignboard, ushort? fence) = _packedSelectedExterior.Unpack();

      void onSelect(string text, ushort? exterior, UInt128? exteriorId)
      {
        if (_unitedExteriorSelection)
        {
          if (exteriorId == null) _packedSelectedExterior = UInt128Extensions.Pack(null, null, null, null, null, null, null, null);
          else _packedSelectedExterior = (UInt128)exteriorId;
          return;
        }

        switch (text)
        {
          case "屋頂":
            roof = exterior;
            break;
          case "外牆":
            walls = exterior;
            break;
          case "窗戶":
            windows = exterior;
            break;
          case "房門":
            door = exterior;
            break;
          case "屋頂裝飾":
            optionalRoof = exterior;
            break;
          case "外牆裝飾":
            optionalWall = exterior;
            break;
          case "門牌":
            optionalSignboard = exterior;
            break;
          case "圍牆":
            fence = exterior;
            break;
        }

        _packedSelectedExterior = UInt128Extensions.Pack(roof, walls, windows, door, optionalRoof, optionalWall, optionalSignboard, fence);
      }

      if (_unitedExteriorSelection)
      {
        DrawSingularExteriorDropdown(plotSize, [ExteriorItemType.UnitedExterior, ExteriorItemType.UnitedExteriorPreset], _packedSelectedExterior, "外觀", onSelect);
      }
      else
      {
        if (DrawSingularExteriorDropdown(plotSize, [ExteriorItemType.Roof], roof, "屋頂", onSelect)) invalidSelection = true;
        if (DrawSingularExteriorDropdown(plotSize, [ExteriorItemType.Walls], walls, "外牆", onSelect)) invalidSelection = true;
        if (DrawSingularExteriorDropdown(plotSize, [ExteriorItemType.Windows], windows, "窗戶", onSelect)) invalidSelection = true;
        if (DrawSingularExteriorDropdown(plotSize, [ExteriorItemType.Door], door, "房門", onSelect)) invalidSelection = true;
        if (DrawSingularExteriorDropdown(plotSize, [ExteriorItemType.OptionalRoof], optionalRoof, "屋頂裝飾", onSelect, true)) invalidSelection = true;
        if (DrawSingularExteriorDropdown(plotSize, [ExteriorItemType.OptionalWall], optionalWall, "外牆裝飾", onSelect, true)) invalidSelection = true;
        if (DrawSingularExteriorDropdown(plotSize, [ExteriorItemType.OptionalSignboard], optionalSignboard, "門牌", onSelect, true)) invalidSelection = true;
        if (DrawSingularExteriorDropdown(plotSize, [ExteriorItemType.Fence], fence, "圍牆", onSelect, true)) invalidSelection = true;
      }
    }

    return invalidSelection;
  }

  private bool DrawSingularExteriorDropdown(PlotSize? plotSize, ExteriorItemType[] types, UInt128? exterior, string text, Action<string, ushort?, UInt128?> onSelect, bool allowNone = false)
  {
    short? plotExterior = _exteriorService.GetPlotExterior((sbyte)((_selectedPlot ?? 0) - 1), types[0]);
    ExteriorItem? plotExteriorItem = ExteriorItems.FirstOrNull(item => item.Id == (UInt128)(plotExterior ?? 0) && types.Contains(item.Type));

    string? selectedExteriorItemName = ExteriorItems.FirstOrNull(item => item.Id == exterior && types.Contains(item.Type))?.Name ?? null;
    ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
    bool invalidSelection = selectedExteriorItemName == null && exterior != 0;

    if (invalidSelection && plotExteriorItem != null && plotExteriorItem.HasValue)
    {
      onSelect(text, (ushort)plotExteriorItem.Value.Id, null);
    }
    else if (invalidSelection && allowNone)
    {
      onSelect(text, 0, null);
    }

    using (ImRaii.IEndObject dropdown = ImRaii.Combo($"##ExteriorSelect{text}", selectedExteriorItemName ?? (exterior == 0 ? $"無{text}" : _unitedExteriorSelection ? $"原有{text}" : plotExteriorItem != null && plotExteriorItem.HasValue ? plotExteriorItem.Value.Name.ToString() : $"選擇{text}…")))
    {
      if (!dropdown.Success) return invalidSelection;

      if (_unitedExteriorSelection)
      {
        ISharedImmediateTexture? icon2 = GetExteriorIcon((sbyte)((_selectedPlot ?? 0) - 1), null).icon;
        if (icon2 == null) return invalidSelection;
        ImGui.Image(icon2.GetWrapOrEmpty().Handle, ScaledVector2(16));
        ImGui.SameLine();
        if (ImGui.Selectable($"原有{text}", exterior == null))
        {
          onSelect(text, null, null);
        }
      }

      if (allowNone)
      {
        ImGui.Dummy(ScaledVector2(16));
        ImGui.SameLine();
        if (ImGui.Selectable($"無{text}", exterior == 0))
        {
          onSelect(text, 0, null);
        }
      }

      foreach (ExteriorItem exteriorItem in ExteriorItems.Where(item => (item.Size == plotSize || item.Size == null) && types.Contains(item.Type)))
      {
        if (!_textureProvider.TryGetFromGameIcon(new GameIconLookup(exteriorItem.Icon), out ISharedImmediateTexture? icon)) continue;
        ImGui.Image(icon.GetWrapOrEmpty().Handle, ScaledVector2(16));
        ImGui.SameLine();
        if (ImGui.Selectable(exteriorItem.Name, exterior == exteriorItem.Id))
        {
          onSelect(text, (ushort)exteriorItem.Id, exteriorItem.Id);
        }
      }
    }

    return invalidSelection;
  }

  private void DrawStainDropdown()
  {
    using (ImRaii.Disabled(_selectedPlot == null))
    {
      (ushort? roofStain, ushort? wallsStain, ushort? windowsStain, ushort? doorStain, ushort? optionalRoofStain, ushort? optionalWallStain, ushort? optionalSignboardStain, ushort? fenceStain) = _packedSelectedStain.Unpack();

      void onSelect(string text, ushort? stain)
      {
        if (_unitedExteriorSelection)
        {
          _packedSelectedStain = UInt128Extensions.Pack(stain, stain, stain, stain, stain, stain, stain, stain);
          return;
        }

        switch (text)
        {
          case "屋頂":
            roofStain = stain;
            break;
          case "外牆":
            wallsStain = stain;
            break;
          case "窗戶":
            windowsStain = stain;
            break;
          case "房門":
            doorStain = stain;
            break;
          case "屋頂裝飾":
            optionalRoofStain = stain;
            break;
          case "外牆裝飾":
            optionalWallStain = stain;
            break;
          case "門牌":
            optionalSignboardStain = stain;
            break;
          case "圍牆":
            fenceStain = stain;
            break;
        }

        _packedSelectedStain = UInt128Extensions.Pack(roofStain, wallsStain, windowsStain, doorStain, optionalRoofStain, optionalWallStain, optionalSignboardStain, fenceStain);
      }

      if (_unitedExteriorSelection)
      {
        DrawSingularStainDropdown(roofStain, string.Empty, onSelect);
      }
      else
      {
        DrawSingularStainDropdown(roofStain, "屋頂", onSelect);
        DrawSingularStainDropdown(wallsStain, "外牆", onSelect);
        DrawSingularStainDropdown(windowsStain, "窗戶", onSelect);
        DrawSingularStainDropdown(doorStain, "房門", onSelect);
        DrawSingularStainDropdown(optionalRoofStain, "屋頂裝飾", onSelect);
        DrawSingularStainDropdown(optionalWallStain, "外牆裝飾", onSelect);
        DrawSingularStainDropdown(optionalSignboardStain, "門牌", onSelect);
        DrawSingularStainDropdown(fenceStain, "圍牆", onSelect);
      }
    }
  }

  private void DrawSingularStainDropdown(ushort? stain, string text, Action<string, ushort?> onSelect)
  {
    Stain? selectedStain = _dataManager.GetExcelSheet<Stain>().GetRowOrDefault(stain ?? 0);
    string selectedStainString = stain == null || selectedStain == null || !selectedStain.HasValue ? text.IsNullOrEmpty() ? "原有顏色" : $"{text}原有顏色" : selectedStain.Value.RowId == 0 ? $"{text}未染色" : $"{selectedStain.Value.Name} {text}";
    ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
    using (ImRaii.IEndObject dropdown = ImRaii.Combo($"##StainSelect{text}", selectedStainString))
    {
      if (!dropdown.Success) return;

      DrawColorCircle(null, 16);
      ImGui.Dummy(ScaledVector2(16));
      ImGui.SameLine();
      if (ImGui.Selectable("原有顏色", stain == null))
      {
        onSelect(text, null);
      }

      foreach (Stain stainRow in _dataManager.GetExcelSheet<Stain>())
      {
        // API 13 names the housing-applicable flag (packed bool 1) Unknown2.
        if (stainRow.RowId != 0 && (stainRow.Name.IsEmpty || !stainRow.Unknown2)) continue;

        DrawColorCircle(stainRow.RowId, 16);
        ImGui.Dummy(ScaledVector2(16));
        ImGui.SameLine();

        if (ImGui.Selectable(stainRow.RowId == 0 ? "未染色" : stainRow.Name.ToString(), stain == stainRow.RowId))
        {
          onSelect(text, (ushort)stainRow.RowId);
        }
      }
    }
  }
}

public struct ExteriorItem
{
  public required PlotSize? Size;
  public required ExteriorItemType Type;
  public required UInt128 Id;
  public required string Name;
  public required ushort Icon;
  public required uint? UnitedExteriorId;
}

public struct Festival
{
  public required ushort Id { get; set; }
  public required string Name { get; set; }
}

public enum OverlayContent
{
  FacadeLocations,
  LoadPreset,
  SavePreset,
  SaveNewPreset,
  FacadeImport,
}
