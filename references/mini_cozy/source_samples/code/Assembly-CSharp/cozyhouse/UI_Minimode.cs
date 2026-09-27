using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using TS;
using TS.UI;
using TS.Window;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Serialization;
using UnityEngine.UI;
using cozyhouse.data;
using cozyhouse.musicplayer;

namespace cozyhouse;

public class UI_Minimode : MovableUI
{
	private UIManager _uiManager;

	[SerializeField]
	private Button _buttonShowSubPanel;

	[SerializeField]
	private Button _buttonPlay;

	[SerializeField]
	private Button _buttonPause;

	[SerializeField]
	private Button _buttonPlay_Deactive;

	[SerializeField]
	private Button _buttonPrev;

	[SerializeField]
	private Button _buttonNext;

	[SerializeField]
	private Button _buttonPrev_Deactive;

	[SerializeField]
	private Button _buttonNext_Deactive;

	[SerializeField]
	private ToggleButton _toggleAmbience;

	[SerializeField]
	private Button _buttonMaximize;

	[SerializeField]
	private ToggleButton _toggleButtonPin;

	[SerializeField]
	private ToggleButton _toggleButtonZoomIn;

	[SerializeField]
	private ToggleButton _toggleButtonZoomOut;

	[SerializeField]
	private ToggleButton _toggleButtonEmbeddedYouTube;

	[SerializeField]
	private ToggleButton _toggleButtonMemo;

	[SerializeField]
	private ToggleButton _toggleButtonTodolist;

	[SerializeField]
	private ToggleButton _toggleButtonTimer;

	[SerializeField]
	private Button _buttonHideSubPanel;

	[SerializeField]
	private GameObject _subPanel;

	[SerializeField]
	private GameObject _objBox;

	[SerializeField]
	private List<Image> _imageBoxes = new List<Image>();

	[SerializeField]
	private Button _buttonBoxBubble;

	[SerializeField]
	private Sprite _spriteBoxOn;

	[SerializeField]
	private Sprite _spriteBoxOff;

	private bool _shouldShowBox = true;

	[SerializeField]
	private CharacterUI_MiniMode _characterUi;

	[Header("타이머 슬라이더")]
	[SerializeField]
	private Minimode_TimerSlider _miniModeTimerSlider = new Minimode_TimerSlider();

	private bool _isInit;

	[FormerlySerializedAs("_button_Tray")]
	[Header("미니 위젯 관련")]
	[SerializeField]
	private Button _button_Widget;

	private Sequence effectSequence;

	private AmbiencePlayer ambiencePlayer;

	public CharacterUI_MiniMode CharacterUi => _characterUi;

	protected override void Awake()
	{
		base.Awake();
		Player instance = Player.Instance;
		_uiManager = Singleton<UIManager>.Instance;
		ambiencePlayer = UnityEngine.Object.FindAnyObjectByType<AmbiencePlayer>();
		_isWrappedPosition = true;
		_buttonShowSubPanel.onClick.AddListener(ShowSubPanel);
		_buttonPlay.onClick.AddListener(PlayBgm);
		_buttonPause.onClick.AddListener(PauseBgm);
		_buttonPrev.onClick.AddListener(PrevBgm);
		_buttonNext.onClick.AddListener(NextBgm);
		_toggleAmbience.OnClick.AddListener(ToggleMuteAmbience);
		AmbiencePlayer obj = ambiencePlayer;
		obj.OnMuteAmbient = (Action)Delegate.Combine(obj.OnMuteAmbient, (Action)delegate
		{
			_toggleAmbience.Toggle(isOn: false);
			_toggleAmbience.GetComponent<TooltipTrigger>().SetTooltipIndex(0);
		});
		AmbiencePlayer obj2 = ambiencePlayer;
		obj2.OnUnMuteAmbient = (Action)Delegate.Combine(obj2.OnUnMuteAmbient, (Action)delegate
		{
			_toggleAmbience.Toggle(isOn: true);
			_toggleAmbience.GetComponent<TooltipTrigger>().SetTooltipIndex(1);
		});
		_buttonMaximize.onClick.AddListener(Maximize);
		_toggleButtonZoomIn.OnClick.AddListener(OnClickZoomIn);
		_toggleButtonZoomOut.OnClick.AddListener(OnClickZoomOut);
		_toggleButtonEmbeddedYouTube.OnClick.AddListener(OnClickEmbedded);
		_toggleButtonPin.OnClick.AddListener(OnClickPin);
		MemoManager memoManager = Singleton<MemoManager>.Instance;
		TodoListManager todoListManager = Singleton<TodoListManager>.Instance;
		memoManager.IsActivate.Subscribe(delegate(bool isOn)
		{
			_toggleButtonMemo.Toggle(isOn);
		});
		todoListManager.IsActivate.Subscribe(delegate(bool isOn)
		{
			_toggleButtonTodolist.Toggle(isOn);
		});
		_toggleButtonMemo.OnClick.AddListener(delegate
		{
			memoManager.Toggle();
		});
		_toggleButtonTodolist.OnClick.AddListener(delegate
		{
			todoListManager.Toggle();
		});
		_toggleButtonTimer.OnClick.AddListener(delegate
		{
			_uiManager.ui_TimerManager.Toggle();
			_toggleButtonTimer.Toggle(_uiManager.ui_TimerManager.IsActive.Value);
		});
		_buttonHideSubPanel.onClick.AddListener(HideSubPanel);
		_buttonBoxBubble.onClick.AddListener(OnClickBoxBubble);
		instance.OnExpCleared = (Action)Delegate.Combine(instance.OnExpCleared, new Action(UpdateView));
		instance.OnLevelUp = (Action<int>)Delegate.Combine(instance.OnLevelUp, new Action<int>(OnLevelUp));
		_miniModeTimerSlider.Init(this);
		instance.SettingSystem.OnIconLocationChanged += delegate(EIconLocation mode)
		{
			bool active = mode == EIconLocation.AlwaysShowInTray || mode == EIconLocation.OnlyMiniMode;
			_button_Widget.gameObject.SetActive(active);
		};
		_button_Widget.onClick.AddListener(delegate
		{
			HideWidgetAnim().Forget();
		});
	}

	private void Start()
	{
		InitializeMovableUI();
		InitView();
		bool isAlwaysOnTopMiniMode = Player.Instance.SettingSystem.IsAlwaysOnTopMiniMode;
		_toggleButtonPin.Toggle(isAlwaysOnTopMiniMode);
		_toggleButtonPin.GetComponent<TooltipTrigger>().SetTooltipIndex((!isAlwaysOnTopMiniMode) ? 1 : 0);
		_toggleButtonEmbeddedYouTube.Toggle(_uiManager.ui_embedded.gameObject.activeInHierarchy);
		TimerUIManager ui_TimerManager = _uiManager.ui_TimerManager;
		ui_TimerManager.OnShow = (Action)Delegate.Combine(ui_TimerManager.OnShow, (Action)delegate
		{
			_toggleButtonTimer.Toggle(isOn: true);
		});
		TimerUIManager ui_TimerManager2 = _uiManager.ui_TimerManager;
		ui_TimerManager2.OnHide = (Action)Delegate.Combine(ui_TimerManager2.OnHide, (Action)delegate
		{
			_toggleButtonTimer.Toggle(isOn: false);
		});
		_buttonPlay_Deactive.onClick.AddListener(ShowEB);
		_buttonPrev_Deactive.onClick.AddListener(ShowEB);
		_buttonNext_Deactive.onClick.AddListener(ShowEB);
		_characterUi.Init();
		_isInit = true;
		UpdateCharacter();
		UpdateView();
		UpdateZoomToggleButton();
		UpdateMusicTooltip(Singleton<MusicPlayer>.Instance.CurrentMusic);
		MusicPlayer instance = Singleton<MusicPlayer>.Instance;
		instance.OnPlayTheMusic = (Action<Music>)Delegate.Combine(instance.OnPlayTheMusic, new Action<Music>(UpdateMusicTooltip));
		instance.OnPauseMusic = (Action)Delegate.Combine(instance.OnPauseMusic, (Action)delegate
		{
			_buttonPause.gameObject.SetActive(value: false);
			_buttonPlay.gameObject.SetActive(value: true);
		});
		instance.OnPlayMusic = (Action)Delegate.Combine(instance.OnPlayMusic, (Action)delegate
		{
			_buttonPause.gameObject.SetActive(value: true);
			_buttonPlay.gameObject.SetActive(value: false);
		});
		EIconLocation iconLocation = Player.Instance.SettingSystem.IconLocation;
		bool active = iconLocation == EIconLocation.AlwaysShowInTray || iconLocation == EIconLocation.OnlyMiniMode;
		_button_Widget.gameObject.SetActive(active);
	}

	public void EarlyInitEvent()
	{
		Debug.Log("미니모드 빠른 이벤트 초기화");
		GameManager instance = Singleton<GameManager>.Instance;
		instance.OnGameModeChanged = (Action<EGameMode>)Delegate.Combine(instance.OnGameModeChanged, (Action<EGameMode>)delegate(EGameMode gameMode)
		{
			if (gameMode == EGameMode.Main)
			{
				OnExitMiniMode();
			}
		});
	}

	private void ShowEB()
	{
		Singleton<UIManager>.Instance.ui_embedded.Show();
	}

	private void UpdateMusicTooltip(Music music)
	{
		if (music == null)
		{
			_buttonPlay.GetComponent<TooltipTrigger>().SetTooltipText("");
			_buttonPause.GetComponent<TooltipTrigger>().SetTooltipText("");
		}
		else
		{
			_buttonPlay.GetComponent<TooltipTrigger>().SetTooltipText(music.MusicName);
			_buttonPause.GetComponent<TooltipTrigger>().SetTooltipText(music.MusicName);
		}
	}

	private void OnEnable()
	{
		_toggleButtonMemo.Toggle(Singleton<MemoManager>.Instance.IsActivate.Value);
		_toggleButtonTodolist.Toggle(Singleton<TodoListManager>.Instance.IsActivate.Value);
		_toggleButtonTimer.Toggle(_uiManager.ui_TimerManager.IsActive.Value);
		_toggleButtonEmbeddedYouTube.Toggle(_uiManager.ui_embedded.gameObject.activeInHierarchy);
		if (_isInit)
		{
			HideSubPanel();
			UpdateCharacter();
			UpdateView();
			UpdateZoomToggleButton();
		}
	}

	private void UpdateCharacter()
	{
		CharacterParts curCharacterParts = Player.Instance.SkinHandler.CurCharacterParts;
		_characterUi.ChangeSkin(curCharacterParts.body);
		_characterUi.ChangeSkin(curCharacterParts.hair);
		_characterUi.ChangeSkin(curCharacterParts.eyes);
		_characterUi.ChangeSkin(curCharacterParts.mouth);
		_characterUi.ChangeSkin(curCharacterParts.hat);
		_characterUi.ChangeSkin(curCharacterParts.accessory1);
		_characterUi.ChangeSkin(curCharacterParts.accessory2);
		_characterUi.UpdateAnimation();
	}

	public void UpdateView()
	{
		if (Singleton<MusicPlayer>.Instance.IsPlayingEmbeddedBrowser)
		{
			_buttonPlay.gameObject.SetActive(value: false);
			_buttonPause.gameObject.SetActive(value: false);
			_buttonPrev.gameObject.SetActive(value: false);
			_buttonNext.gameObject.SetActive(value: false);
			_buttonPlay_Deactive.gameObject.SetActive(value: true);
			_buttonPrev_Deactive.gameObject.SetActive(value: true);
			_buttonNext_Deactive.gameObject.SetActive(value: true);
		}
		else
		{
			_buttonPlay_Deactive.gameObject.SetActive(value: false);
			_buttonPrev_Deactive.gameObject.SetActive(value: false);
			_buttonNext_Deactive.gameObject.SetActive(value: false);
			if (Singleton<MusicPlayer>.Instance.IsPlaying)
			{
				_buttonPause.gameObject.SetActive(value: true);
				_buttonPlay.gameObject.SetActive(value: false);
			}
			else
			{
				_buttonPause.gameObject.SetActive(value: false);
				_buttonPlay.gameObject.SetActive(value: true);
			}
			_buttonPrev.gameObject.SetActive(value: true);
			_buttonNext.gameObject.SetActive(value: true);
		}
		_toggleAmbience.Toggle(!ambiencePlayer.IsMute);
		_toggleAmbience.GetComponent<TooltipTrigger>().SetTooltipIndex((!ambiencePlayer.IsMute) ? 1 : 0);
		if (Player.Instance.CanLevelUp())
		{
			UpdateBoxVisibility((_miniModeTimerSlider.SessionTimerSystem == null) ? ETimerStatus.Session : _miniModeTimerSlider.SessionTimerSystem.CurrentStatus.Value);
		}
		else
		{
			_objBox.SetActive(value: false);
		}
	}

	private void ShowSubPanel()
	{
		_subPanel.SetActive(value: true);
	}

	public void HideSubPanel()
	{
		_subPanel.SetActive(value: false);
	}

	private void OnClickZoomIn()
	{
		Singleton<GameManager>.Instance.ZoomIn_MiniMode();
		if (Singleton<CameraManager>.Instance.CurrentZoomLevel == ZoomLevel.Zoom1)
		{
			ResetAnimator();
		}
		UpdateZoomToggleButton();
	}

	private void OnClickZoomOut()
	{
		Singleton<GameManager>.Instance.ZoomOut_MiniMode();
		UpdateZoomToggleButton();
	}

	private void OnClickEmbedded()
	{
		if (Singleton<UIManager>.Instance.ui_embedded.gameObject.activeInHierarchy)
		{
			Singleton<UIManager>.Instance.ui_embedded.Hide();
			_toggleButtonEmbeddedYouTube.Toggle(isOn: false);
		}
		else
		{
			Singleton<UIManager>.Instance.ui_embedded.Show();
			_toggleButtonEmbeddedYouTube.Toggle(isOn: true);
			UpdateView();
		}
	}

	private void UpdateZoomToggleButton()
	{
		_toggleButtonZoomIn.Toggle(!Singleton<CameraManager>.Instance.IsMaxZoomIn);
		_toggleButtonZoomOut.Toggle(!Singleton<CameraManager>.Instance.IsMaxZoomOut);
	}

	private void OnClickPin()
	{
		bool flag = !Player.Instance.SettingSystem.IsAlwaysOnTopMiniMode;
		Player.Instance.SettingSystem.SetAlwaysOnTopMiniMode(flag);
		Singleton<WindowManager>.Instance.SetAlwaysOnTop(flag);
		_toggleButtonPin.Toggle(flag);
		TooltipTrigger component = _toggleButtonPin.GetComponent<TooltipTrigger>();
		component.SetTooltipIndex((!flag) ? 1 : 0);
		component.OnPointerEnter(null);
	}

	protected void InitializeMovableUI()
	{
		Vector2 minimodePosition = Player.Instance.SettingSystem.MinimodePosition;
		InitPosition(minimodePosition);
	}

	public void ResetPosition()
	{
		Vector2 vector = new Vector2(Const.UI_MINIMODE.InitialPosition.x, Const.UI_MINIMODE.InitialPosition.y);
		InitPosition(vector);
		Player.Instance.SettingSystem.SetMinimodePosition(vector);
	}

	private void PlayBgm()
	{
		Singleton<MusicPlayer>.Instance.ResumeBgm();
		_buttonPause.gameObject.SetActive(value: true);
		_buttonPlay.gameObject.SetActive(value: false);
	}

	private void PauseBgm()
	{
		Singleton<MusicPlayer>.Instance.PauseBgm();
		_buttonPause.gameObject.SetActive(value: false);
		_buttonPlay.gameObject.SetActive(value: true);
	}

	private void PrevBgm()
	{
		Singleton<MusicPlayer>.Instance.PlayPreviousTrack();
	}

	private void NextBgm()
	{
		Singleton<MusicPlayer>.Instance.PlayNextTrack();
	}

	private void ToggleMuteAmbience()
	{
		TooltipTrigger component = _toggleAmbience.GetComponent<TooltipTrigger>();
		if (ambiencePlayer.IsMute)
		{
			ambiencePlayer.UnmuteAmbientSound();
			component.SetTooltipIndex(1);
		}
		else
		{
			ambiencePlayer.MuteAmbientSound();
			component.SetTooltipIndex(0);
		}
		component.OnPointerEnter(null);
	}

	public void UpdateView_LevelUpBox()
	{
		for (int i = 0; i < _imageBoxes.Count; i++)
		{
			if (i < Player.Instance.GetCountCanLevelUp())
			{
				_imageBoxes[i].sprite = _spriteBoxOn;
			}
			else
			{
				_imageBoxes[i].sprite = _spriteBoxOff;
			}
		}
	}

	private void OnLevelUp(int level)
	{
		UpdateView();
	}

	private void OnClickBoxBubble()
	{
		if (!Singleton<CameraManager>.Instance.IsMaxZoomOut)
		{
			Player.Instance.TryLevelUp();
		}
		else
		{
			Maximize();
		}
	}

	public void UpdateBoxVisibility(ETimerStatus status = ETimerStatus.Ready)
	{
		EBoxVisibleMode boxVisibleMode = Player.Instance.SettingSystem.BoxVisibleMode;
		bool flag = Player.Instance.CanLevelUp();
		switch (boxVisibleMode)
		{
		case EBoxVisibleMode.AlwaysVisible:
			_shouldShowBox = true;
			break;
		case EBoxVisibleMode.HideOnSession:
			_shouldShowBox = status != ETimerStatus.Session;
			break;
		case EBoxVisibleMode.AlwaysHidden:
			_shouldShowBox = false;
			break;
		}
		bool flag2 = flag && _shouldShowBox;
		if (_objBox.activeSelf != flag2)
		{
			_objBox.SetActive(flag2);
			if (flag2)
			{
				UpdateView_LevelUpBox();
			}
		}
	}

	private void Maximize()
	{
		Singleton<GameManager>.Instance.ChangeGameMode(EGameMode.Main);
	}

	private void OnExitMiniMode()
	{
		ResetCameraPosition();
		ResetAnimator();
	}

	private void ResetCameraPosition()
	{
	}

	private void ResetAnimator()
	{
		Singleton<World>.Instance.characterScript.ResetAnim();
		Singleton<World>.Instance.petScript.ResetAnimation();
		Singleton<World>.Instance.CurrentRoom.ResetAnimation();
	}

	public override void OnPointerUp(PointerEventData eventData)
	{
		base.OnPointerUp(eventData);
		Save();
	}

	protected override void Save()
	{
		base.Save();
		Player.Instance.SettingSystem.SetMinimodePosition(rectTransform.anchoredPosition);
	}

	private async UniTaskVoid HideWidgetAnim()
	{
		_subPanel.SetActive(value: false);
		if (effectSequence != null && effectSequence.IsActive())
		{
			effectSequence.Kill();
		}
		Vector3 vector = new Vector2(148f, 0f);
		effectSequence = DOTween.Sequence();
		effectSequence.Append(rectTransform.DOAnchorPos(vector, 0.2f).SetEase(Ease.InBack));
		effectSequence.Join(rectTransform.DOScale(0f, 0.2f).SetEase(Ease.InQuart));
		await effectSequence;
		Singleton<UIManager>.Instance.CanvasMiniMode.HideMiniWidget();
		rectTransform.anchoredPosition = Const.UI_MINIMODE.InitialPosition;
		rectTransform.localScale = Vector3.one;
	}
}
