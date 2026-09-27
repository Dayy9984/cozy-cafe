using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using Sirenix.Utilities;
using TS;
using UnityEngine;
using cozyhouse;
using cozyhouse.data;
using cozyhouse.util;

public class MemoManager : Singleton<MemoManager>
{
	[SerializeField]
	private Transform memoHolder;

	[SerializeField]
	private GameObject _memoPrefab;

	private Dictionary<int, MemoCache> _cacheDic = new Dictionary<int, MemoCache>();

	private Queue<int> _availableKeys = new Queue<int>();

	private Dictionary<int, UI_Memo> _activatedMemos = new Dictionary<int, UI_Memo>();

	private UIBringToFront _uiBringToFront;

	private int _nextKey;

	private int _curCreateKey = int.MinValue;

	private ObservableValue<bool> _isActivate = new ObservableValue<bool>(initialValue: false);

	private bool _beforeMiniModeActive;

	private bool _isNewTabMode = true;

	private const string POOLING_KEY = "UI_Memo";

	private const int MAX = 3;

	private bool needMigration;

	private float _monitorWidth;

	public bool IsNewTabMode => _isNewTabMode;

	public ObservableValue<bool> IsActivate => _isActivate;

	public IReadOnlyDictionary<int, MemoCache> CacheDic => _cacheDic;

	public event Action<int> OnAddMemo;

	public event Action<int> OnDeleteMemo;

	public event Action<int> OnCloseMemo;

	public event Action<int> OnOpenMemo;

	public new void Init()
	{
		SettingSystem settingSystem = Player.Instance.SettingSystem;
		_isActivate.Value = settingSystem.IsMemoOn;
		ImportData(Player.Instance.UserData.memoUserDatas);
		_isNewTabMode = settingSystem.IsNewMemoTabMode;
		_uiBringToFront = memoHolder.GetComponent<UIBringToFront>();
		GameManager gameManager = Singleton<GameManager>.Instance;
		gameManager.OnGameModeChanged = (Action<EGameMode>)Delegate.Combine(gameManager.OnGameModeChanged, (Action<EGameMode>)delegate(EGameMode mode)
		{
			switch (mode)
			{
			case EGameMode.MiniMode:
				if (Player.Instance.SettingSystem.IsCloseAllPopupMiniMode)
				{
					_beforeMiniModeActive = _isActivate.Value;
				}
				break;
			case EGameMode.Main:
				if (Player.Instance.SettingSystem.IsCloseAllPopupMiniMode && _beforeMiniModeActive && !_isActivate.Value)
				{
					Toggle();
				}
				break;
			}
		});
		WaitForInit().Forget();
	}

	private async UniTaskVoid WaitForInit()
	{
		await UniTask.WaitUntil(() => Singleton<GameManager>.Instance.IsInit);
		ObjectPoolManager objectPoolManager = Singleton<ObjectPoolManager>.Instance;
		List<int> list = new List<int>();
		RectTransform rt = null;
		bool check = false;
		foreach (KeyValuePair<int, MemoCache> item in _cacheDic)
		{
			var (key, memoCache2) = (KeyValuePair<int, MemoCache>)(ref item);
			list.Add(key);
			if (!memoCache2.IsOpen || !_isActivate.Value || !_isNewTabMode)
			{
				continue;
			}
			objectPoolManager.Spawn("UI_Memo", delegate(GameObject val)
			{
				UI_Memo component = val.GetComponent<UI_Memo>();
				component.SetParent(memoHolder);
				_activatedMemos.Add(key, component);
				component.OnSelect += SetMemoHierarchyIndex;
				component.SetData(_cacheDic[key]);
				if (needMigration)
				{
					if ((object)rt != null)
					{
						component.rectTransform.anchoredPosition = UIGridPositioner.GetGridPosition(component.rectTransform, rt, Const.UI_MEMO.InitialSize, Const.UI_MEMO.InitialPosition, memoHolder);
					}
					rt = component.rectTransform;
					check = true;
				}
				component.Show();
			});
		}
		if (needMigration && check)
		{
			needMigration = false;
		}
		SetAvailableKeys(list);
	}

	private void SetAvailableKeys(List<int> existingKeys)
	{
		_availableKeys.Clear();
		HashSet<int> hashSet = new HashSet<int>(existingKeys);
		int num = -1;
		if (existingKeys.Count > 0)
		{
			num = existingKeys.Max();
			for (int i = 0; i < num; i++)
			{
				if (!hashSet.Contains(i))
				{
					_availableKeys.Enqueue(i);
				}
			}
		}
		_nextKey = num + 1;
		Debug.Log($"[MemoManager] SetAvailableKeys - 사용 가능한 키: {_availableKeys.Count}개, 다음 키: {_nextKey}");
	}

	public bool IsMaxAmount()
	{
		return _cacheDic.Count >= 3;
	}

	public void Toggle()
	{
		_isActivate.Value = !_isActivate.Value;
		Singleton<UIManager>.Instance.ui_main.button_memo.Toggle(_isActivate.Value);
		SettingSystem settingSystem = Player.Instance.SettingSystem;
		settingSystem.SetMemoOn(_isActivate.Value);
		ObjectPoolManager objectPoolManager = Singleton<ObjectPoolManager>.Instance;
		if (!_isNewTabMode)
		{
			return;
		}
		if (_isActivate.Value)
		{
			bool hasOpenMemo = false;
			RectTransform rt = null;
			foreach (KeyValuePair<int, MemoCache> item in _cacheDic)
			{
				var (key, data) = (KeyValuePair<int, MemoCache>)(ref item);
				if (!needMigration && !data.IsOpen)
				{
					continue;
				}
				if (!_activatedMemos.TryGetValue(key, out var value))
				{
					objectPoolManager.Spawn("UI_Memo", delegate(GameObject val)
					{
						UI_Memo component = val.GetComponent<UI_Memo>();
						component.SetParent(memoHolder);
						_activatedMemos.Add(key, component);
						component.OnSelect += SetMemoHierarchyIndex;
						data.IsOpen = true;
						hasOpenMemo = true;
						component.SetData(_cacheDic[key]);
						if (needMigration)
						{
							if ((object)rt != null)
							{
								component.rectTransform.anchoredPosition = UIGridPositioner.GetGridPosition(component.rectTransform, rt, Const.UI_MEMO.InitialSize, Const.UI_MEMO.InitialPosition, memoHolder);
							}
							rt = component.rectTransform;
						}
						component.Show();
					});
				}
				else
				{
					data.IsOpen = true;
					hasOpenMemo = true;
					value.Show();
				}
			}
			if (needMigration)
			{
				needMigration = false;
			}
			if (hasOpenMemo)
			{
				return;
			}
			if (_cacheDic.Count > 0)
			{
				if (_cacheDic.ContainsKey(settingSystem.LastClosedMemoKey))
				{
					OpenMemo(settingSystem.LastClosedMemoKey);
					return;
				}
				MemoCache memoCache2 = _cacheDic.Values.OrderBy((MemoCache cache) => cache.SortOrder).FirstOrDefault();
				OpenMemo(memoCache2.Key);
			}
			else
			{
				AddNewMemo();
			}
			return;
		}
		foreach (KeyValuePair<int, UI_Memo> activatedMemo in _activatedMemos)
		{
			activatedMemo.Value.Hide();
		}
		Singleton<UIManager>.Instance.UINote.OffNote(eNoteTabType.Memo);
	}

	public void OffAllMemo()
	{
		_isActivate.Value = true;
		Toggle();
	}

	private int GetNewKey()
	{
		if (_availableKeys.Count > 0)
		{
			return _availableKeys.Dequeue();
		}
		return _nextKey++;
	}

	public void ReSetup()
	{
		foreach (KeyValuePair<int, UI_Memo> activatedMemo in _activatedMemos)
		{
			activatedMemo.Value.ReSetup();
		}
	}

	private void ReleaseKey(int key)
	{
		_availableKeys.Enqueue(key);
	}

	public bool AddNewMemo(int skinKey = 1, bool needGridPos = false)
	{
		if (_cacheDic.Count >= 3)
		{
			Debug.Log("[메모 최대개수 초과]");
			Singleton<ToastManager>.Instance.ShowToast("Demo_Toast_MaxMemo".Localize());
			return false;
		}
		Singleton<ObjectPoolManager>.Instance.Spawn("UI_Memo", delegate(GameObject val)
		{
			UI_Memo component = val.GetComponent<UI_Memo>();
			component.SetParent(memoHolder);
			MemoCache newMemoData = GetNewMemoData();
			newMemoData.IsOpen = true;
			if (needGridPos)
			{
				RectTransform lastCreatedUI = _activatedMemos.Values.OrderByDescending((UI_Memo memo) => memo.transform.GetSiblingIndex()).FirstOrDefault()?.GetComponent<RectTransform>();
				newMemoData.Pos = UIGridPositioner.GetGridPosition(component.rectTransform, lastCreatedUI, Const.UI_MEMO.InitialSize, Const.UI_MEMO.InitialPosition, memoHolder);
			}
			newMemoData.SkinKey = skinKey;
			newMemoData.SortOrder = GetNextSortOrder();
			_activatedMemos.Add(newMemoData.Key, component);
			component.OnSelect += SetMemoHierarchyIndex;
			component.SetData(newMemoData);
			component.Show();
			_curCreateKey = newMemoData.Key;
			this.OnAddMemo?.Invoke(newMemoData.Key);
		});
		return true;
	}

	public MemoCache GetNewMemoData()
	{
		int newKey = GetNewKey();
		return GetCache(newKey);
	}

	private void ImportData(List<MemoUserData> items)
	{
		needMigration = MigrateLegacyData(items);
		HashSet<int> hashSet = new HashSet<int>();
		Dictionary<int, int> dictionary = new Dictionary<int, int>();
		foreach (MemoUserData item in items)
		{
			int num = item.Key;
			if (hashSet.Contains(item.Key))
			{
				int newKey = GetNewKey();
				dictionary[item.Key] = newKey;
				Debug.LogWarning($"[MemoManager] 중복 키 발견: {item.Key} -> 새 키 할당: {newKey}");
				item.SetKey(newKey);
				num = newKey;
			}
			hashSet.Add(num);
			MemoCache memoCache = new MemoCache(item);
			if (!item.IsEditTitle)
			{
				item.SetTitle(memoCache.GetTitle());
			}
			if (!_cacheDic.TryAdd(num, memoCache))
			{
				Debug.LogError($"[MemoManager] 키값 추가 실패: {num}");
			}
			else
			{
				Debug.Log($"[MemoManager] 메모 로드 성공 - 키: {num}");
			}
		}
		if (dictionary.Count > 0)
		{
			Debug.LogWarning($"[MemoManager] 총 {dictionary.Count}개의 중복 키가 새로운 키로 변경됨");
			foreach (KeyValuePair<int, int> item2 in dictionary)
			{
				Debug.LogWarning($"  - 원본 키: {item2.Key} -> 새 키: {item2.Value}");
			}
		}
		SetAvailableKeys(hashSet.ToList());
		ValidateAndReorderData();
	}

	private bool MigrateLegacyData(List<MemoUserData> items)
	{
		if (items == null || items.Count == 0)
		{
			return false;
		}
		bool flag = items.All((MemoUserData item) => item.SortOrder == 0);
		if (flag)
		{
			Debug.Log("[MemoManager] 이전 버전 데이터 감지 - 마이그레이션 수행");
			Player player = Player.Instance;
			EMemoTextSize memoTextSize = player.SettingSystem.MemoTextSize;
			bool isVisualEditorOn = player.SettingSystem.IsVisualEditorOn;
			int selectedUiSkinKey_Memo = player.UserData.playerUserData.selectedUiSkinKey_Memo;
			List<MemoUserData> list = items.OrderBy((MemoUserData item) => item.Key).ToList();
			for (int num = 0; num < list.Count; num++)
			{
				list[num].SetSortOrder(num);
				list[num].SetMemoTextSize(memoTextSize);
				list[num].SetVisualEditorOn(isVisualEditorOn);
				list[num].SetSkinKey(selectedUiSkinKey_Memo);
				list[num].SetOpenState(_isActivate.Value);
			}
		}
		return flag;
	}

	public List<MemoUserData> ExportData()
	{
		return _cacheDic.Select((KeyValuePair<int, MemoCache> x) => x.Value.GetMemoUserData()).ToList();
	}

	public MemoCache GetCache(int key)
	{
		if (_cacheDic.TryGetValue(key, out var value))
		{
			return value;
		}
		MemoUserData memoUserData = new MemoUserData(key, "");
		memoUserData.SetSortOrder(GetNextSortOrder());
		value = new MemoCache(memoUserData);
		_cacheDic.Add(key, value);
		return value;
	}

	public void TryDeleteMemo(int key)
	{
		if (_cacheDic.TryGetValue(key, out var value))
		{
			if (_cacheDic.Count <= 1)
			{
				return;
			}
			Singleton<PopupManager>.Instance.Hide(PopupType.Delete);
			if (value.IsOpen)
			{
				UI_Memo uI_Memo = _activatedMemos[value.Key];
				uI_Memo.OnSelect -= SetMemoHierarchyIndex;
				uI_Memo.Hide();
				_activatedMemos.Remove(value.Key);
				Singleton<ObjectPoolManager>.Instance.DeSpawn(uI_Memo.gameObject);
			}
			ReleaseKey(key);
			_cacheDic.Remove(key);
			this.OnDeleteMemo?.Invoke(key);
			ReorderAfterDelete();
		}
		if (_activatedMemos.Count == 0)
		{
			UIManager uIManager = Singleton<UIManager>.Instance;
			_isActivate.Value = false;
			uIManager.ui_main.button_memo.Toggle(isOn: false);
			Player.Instance.SettingSystem.SetMemoOn(isOn: false);
		}
	}

	public void CloseMemo(int key)
	{
		UI_Memo uI_Memo = _activatedMemos[key];
		uI_Memo.OnSelect -= SetMemoHierarchyIndex;
		uI_Memo.Hide();
		CloseWait(uI_Memo, key).Forget();
	}

	private async UniTaskVoid CloseWait(UI_Memo obj, int key)
	{
		await AnimationAwaiter.WaitForSpecificAnimation(obj.GetComponent<Animator>(), "uiAnimation_close", 0, 0f, obj.GetCancellationTokenOnDestroy());
		Singleton<ObjectPoolManager>.Instance.DeSpawn(obj.gameObject);
		_cacheDic[key].IsOpen = false;
		_activatedMemos.Remove(key);
		SettingSystem settingSystem = Player.Instance.SettingSystem;
		settingSystem.SetLastClosedMemoKey(key);
		this.OnCloseMemo?.Invoke(key);
		if (_activatedMemos.Count == 0)
		{
			UIManager uIManager = Singleton<UIManager>.Instance;
			_isActivate.Value = false;
			uIManager.ui_main.button_memo.Toggle(isOn: false);
			settingSystem.SetMemoOn(isOn: false);
		}
	}

	public void OpenMemo(int key)
	{
		if (_activatedMemos.ContainsKey(key))
		{
			_activatedMemos[key].Show();
			return;
		}
		Singleton<ObjectPoolManager>.Instance.Spawn("UI_Memo", delegate(GameObject val)
		{
			UI_Memo component = val.GetComponent<UI_Memo>();
			component.SetParent(memoHolder);
			component.SetData(_cacheDic[key]);
			_cacheDic[key].IsOpen = true;
			_activatedMemos.Add(key, component);
			component.OnSelect += SetMemoHierarchyIndex;
			component.Show();
			this.OnOpenMemo?.Invoke(key);
		});
	}

	public void CopyMemo(int key)
	{
		if (AddNewMemo())
		{
			if (_curCreateKey == int.MinValue)
			{
				Debug.LogError("최근 생성 키 오류");
				return;
			}
			MemoCache cache = GetCache(_curCreateKey);
			MemoCache cache2 = GetCache(key);
			string title = (cache2.Title.IsNullOrWhitespace() ? cache2.Title : (cache2.Title + "UI_Copied".Localize()));
			cache.Title = title;
			cache.EditContent(cache2.Content);
			cache.SkinKey = cache2.SkinKey;
			cache.Size = cache2.Size;
			cache.MemoTextSize = cache2.MemoTextSize;
			cache.SortOrder = cache2.SortOrder + 1;
			ReorderAfterInsert(cache.SortOrder);
			_activatedMemos[_curCreateKey].SetData(cache);
		}
	}

	private void SetMemoHierarchyIndex(int key)
	{
		UI_Memo uI_Memo = _activatedMemos[key];
		int siblingIndex = memoHolder.childCount - 1;
		uI_Memo.transform.SetSiblingIndex(siblingIndex);
		_uiBringToFront.OnPointDown();
	}

	private int GetNextSortOrder()
	{
		if (_cacheDic.Count == 0)
		{
			return 0;
		}
		return _cacheDic.Values.Max((MemoCache cache) => cache.SortOrder) + 1;
	}

	private void ValidateAndReorderData()
	{
		List<MemoCache> list = _cacheDic.Values.ToList();
		list.Sort((MemoCache a, MemoCache b) => a.SortOrder.CompareTo(b.SortOrder));
		for (int num = 0; num < list.Count; num++)
		{
			list[num].SortOrder = num;
		}
	}

	private void ReorderAfterDelete()
	{
		List<MemoCache> list = _cacheDic.Values.OrderBy((MemoCache cache) => cache.SortOrder).ToList();
		for (int num = 0; num < list.Count; num++)
		{
			list[num].SortOrder = num;
		}
	}

	private void ReorderAfterInsert(int insertOrder)
	{
		foreach (MemoCache value in _cacheDic.Values)
		{
			if (value.SortOrder >= insertOrder && value.SortOrder != insertOrder)
			{
				value.SortOrder++;
			}
		}
	}

	public void UpdateMemoOrder(int key, int newOrder)
	{
		if (!_cacheDic.TryGetValue(key, out var _))
		{
			return;
		}
		List<MemoCache> list = _cacheDic.Values.OrderBy((MemoCache c) => c.SortOrder).ToList();
		int num = list.FindIndex((MemoCache c) => c.Key == key);
		if (num == -1)
		{
			return;
		}
		newOrder = Mathf.Clamp(newOrder, 0, list.Count - 1);
		if (num != newOrder)
		{
			MemoCache item = list[num];
			list.RemoveAt(num);
			list.Insert(newOrder, item);
			for (int num2 = 0; num2 < list.Count; num2++)
			{
				list[num2].SortOrder = num2;
			}
		}
	}

	public List<UserNoteData> GetSortedMemoDataList()
	{
		return ((IEnumerable<MemoCache>)_cacheDic.Values.OrderBy((MemoCache cache) => cache.SortOrder)).Select((Func<MemoCache, UserNoteData>)((MemoCache cache) => cache.GetMemoUserData())).ToList();
	}

	public void ResetSlotPositionAndSize()
	{
		int key;
		UI_Memo value;
		foreach (KeyValuePair<int, UI_Memo> activatedMemo in _activatedMemos)
		{
			activatedMemo.Deconstruct(out key, out value);
			value.ResetDefaultSize();
		}
		int num = 0;
		if (Player.Instance.SettingSystem.CurrentMonitorUserData != null)
		{
			num = Player.Instance.SettingSystem.CurrentMonitorUserData.sidebarSize;
		}
		Vector2 basePosition = Const.UI_MEMO.InitialPosition - new Vector2(num, 0f);
		RectTransform rectTransform = null;
		foreach (KeyValuePair<int, UI_Memo> activatedMemo2 in _activatedMemos)
		{
			activatedMemo2.Deconstruct(out key, out value);
			UI_Memo uI_Memo = value;
			if (!(rectTransform == uI_Memo.rectTransform))
			{
				uI_Memo.rectTransform.anchoredPosition = UIGridPositioner.GetGridPosition(uI_Memo.rectTransform, rectTransform, Const.UI_MEMO.InitialSize, basePosition, memoHolder);
				uI_Memo.SaveData();
				rectTransform = uI_Memo.rectTransform;
			}
		}
	}
}
