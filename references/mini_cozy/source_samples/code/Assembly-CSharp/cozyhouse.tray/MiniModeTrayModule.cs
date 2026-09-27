using System.Collections.Generic;
using TS;
using UnityEngine;
using Utils;

namespace cozyhouse.tray;

public class MiniModeTrayModule : TrayModuleBase
{
	private GameManager gameManager;

	private Canvas_Minimode miniModeCanvas;

	private Texture2D widgetIcon;

	private MenuItemData widgetItem;

	private MenuItemData zoomOffItem;

	private MenuItemData zoom1Item;

	private MenuItemData zoom2Item;

	private MenuItemData zoom3Item;

	private ZoomLevel currentZoomLevel;

	public override string ModuleName => "Room";

	public MiniModeTrayModule(bool enableDebugLogs = false)
		: base(enableDebugLogs)
	{
		widgetIcon = Resources.Load<Texture2D>("Sprites/Tray/Tray_MiniMode");
		miniModeCanvas = Singleton<UIManager>.Instance.CanvasMiniMode;
	}

	protected override bool OnInitialize()
	{
		gameManager = Singleton<GameManager>.Instance;
		if (gameManager == null)
		{
			Debug.LogWarning("[" + ModuleName + "] GameManager.Instance를 찾을 수 없습니다.");
			return false;
		}
		CameraManager instance = Singleton<CameraManager>.Instance;
		currentZoomLevel = instance.CurrentZoomLevel;
		instance.OnChangedZoomLevel += OnExternalZoomLevelChanged;
		miniModeCanvas.OnActive += OnCanvasActivated;
		return true;
	}

	protected override List<MenuItemData> CreateMenuItems()
	{
		List<MenuItemData> list = new List<MenuItemData>();
		if (gameManager.IsInit)
		{
			if (gameManager.CurrentGameMode == EGameMode.Main || Singleton<TrayManager>.Instance.isMinimize)
			{
				return list;
			}
		}
		else
		{
			if (PlayerPrefs.GetInt("StartInMiniMode") != 1)
			{
				return list;
			}
			EIconLocation iconLocation = Player.Instance.SettingSystem.IconLocation;
			if (iconLocation == EIconLocation.OnlyMainMode || iconLocation == EIconLocation.AlwaysShowInTaskbar)
			{
				return list;
			}
		}
		SettingSystem settingSystem = Player.Instance.SettingSystem;
		string label = (settingSystem.IsOnMiniUI ? "Tray_HideMiniWidget".Localize() : "Tray_ShowMiniWidget".Localize());
		widgetItem = new MenuItemData(label, widgetIcon, delegate
		{
			if (settingSystem.IsOnMiniUI)
			{
				miniModeCanvas.HideMiniWidget();
			}
			else
			{
				miniModeCanvas.ShowMiniWidget();
			}
		});
		list.Add(widgetItem);
		zoomOffItem = MenuItemData.CreateCheckable("Tray_Room_Hidden".Localize(), currentZoomLevel == ZoomLevel.Zoom0, delegate
		{
			OnZoomLevelSelected(ZoomLevel.Zoom0);
		});
		zoomOffItem.KeepMenuOpen = false;
		zoom1Item = MenuItemData.CreateCheckable("Tray_Room_Zoom1".Localize(), currentZoomLevel == ZoomLevel.Zoom1, delegate
		{
			OnZoomLevelSelected(ZoomLevel.Zoom1);
		});
		zoom1Item.KeepMenuOpen = false;
		zoom2Item = MenuItemData.CreateCheckable("Tray_Room_Zoom2".Localize(), currentZoomLevel == ZoomLevel.Zoom2, delegate
		{
			OnZoomLevelSelected(ZoomLevel.Zoom2);
		});
		zoom2Item.KeepMenuOpen = false;
		zoom3Item = MenuItemData.CreateCheckable("Tray_Room_Zoom3".Localize(), currentZoomLevel == ZoomLevel.Zoom3, delegate
		{
			OnZoomLevelSelected(ZoomLevel.Zoom3);
		});
		zoom3Item.KeepMenuOpen = false;
		MenuItemData item = new MenuItemData("Tray_ShowRoomMenu".Localize(), new List<MenuItemData> { zoomOffItem, zoom1Item, zoom2Item, zoom3Item });
		list.Add(item);
		list.Add(MenuItemData.CreateSeparator());
		return list;
	}

	private void OnZoomLevelSelected(ZoomLevel newZoomLevel)
	{
		if (currentZoomLevel != newZoomLevel)
		{
			ZoomLevel num = currentZoomLevel;
			currentZoomLevel = newZoomLevel;
			gameManager.ChangeMinimodeZoomLevel(newZoomLevel);
			UpdateMenuCheckStates();
			if (num == ZoomLevel.Zoom0 && newZoomLevel != ZoomLevel.Zoom0)
			{
				OnZoomActivated();
			}
			if (newZoomLevel != ZoomLevel.Zoom0)
			{
				Singleton<TrayManager>.Instance.BringToForeground();
			}
		}
	}

	private void UpdateMenuCheckStates()
	{
		if (zoomOffItem != null)
		{
			zoomOffItem.IsChecked = currentZoomLevel == ZoomLevel.Zoom0;
			UpdateMenuItemCheckState(zoomOffItem);
		}
		if (zoom1Item != null)
		{
			zoom1Item.IsChecked = currentZoomLevel == ZoomLevel.Zoom1;
			UpdateMenuItemCheckState(zoom1Item);
		}
		if (zoom2Item != null)
		{
			zoom2Item.IsChecked = currentZoomLevel == ZoomLevel.Zoom2;
			UpdateMenuItemCheckState(zoom2Item);
		}
		if (zoom3Item != null)
		{
			zoom3Item.IsChecked = currentZoomLevel == ZoomLevel.Zoom3;
			UpdateMenuItemCheckState(zoom3Item);
		}
	}

	private void UpdateMenuItemCheckState(MenuItemData item)
	{
		if (item != null && item.MenuId != 0 && TrayIcon.IsMenuOpen())
		{
			TrayIcon.UpdateMenuItemState(item.MenuId, item.IsEnabled, item.IsChecked);
		}
	}

	public ZoomLevel GetCurrentZoomLevel()
	{
		return currentZoomLevel;
	}

	public void OnExternalZoomLevelChanged(ZoomLevel newLevel)
	{
		if (currentZoomLevel != newLevel)
		{
			_ = currentZoomLevel;
			currentZoomLevel = newLevel;
			UpdateMenuCheckStates();
		}
	}

	private void OnZoomActivated()
	{
		World instance = Singleton<World>.Instance;
		instance.characterScript.ResetAnim();
		instance.petScript.ResetAnimation();
		instance.CurrentRoom.ResetAnimation();
	}

	protected override void OnCleanup()
	{
		gameManager = null;
		zoomOffItem = null;
		zoom1Item = null;
		zoom2Item = null;
		zoom3Item = null;
	}

	private void OnCanvasActivated(bool isOn)
	{
		if (!(miniModeCanvas == null) && widgetItem != null)
		{
			string label = (isOn ? "Tray_HideMiniWidget".Localize() : "Tray_ShowMiniWidget".Localize());
			widgetItem.Label = label;
			if (isOn)
			{
				Singleton<TrayManager>.Instance.BringToForeground();
			}
		}
	}
}
