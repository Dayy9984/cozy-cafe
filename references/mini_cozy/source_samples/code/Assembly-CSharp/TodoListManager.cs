using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Sirenix.Utilities;
using TS;
using UnityEngine;
using cozyhouse;
using cozyhouse.data;
using cozyhouse.util;

public class TodoListManager : Singleton<TodoListManager>
{
	[SerializeField]
	private Transform listHolder;

	[SerializeField]
	private GameObject prefab;

	private Dictionary<int, TodolistCache> _cacheDic = new Dictionary<int, TodolistCache>();

	private Queue<int> _availableKeys = new Queue<int>();

	private Dictionary<int, UI_TodoList> _activatedTodoList = new Dictionary<int, UI_TodoList>();

	private UIBringToFront _uiBringToFront;

	private int _nextKey;

	private int _curCreateKey = int.MinValue;

	private bool _beforeMiniModeActive;

	private ObservableValue<bool> _isActivate = new ObservableValue<bool>(initialValue: false);

	private const string POOLING_KEY = "UI_TodoList";

	private const int MAX_GROUP = 2;

	public IReadOnlyDictionary<int, TodolistCache> CacheDic => _cacheDic;

	public ObservableValue<bool> IsActivate => _isActivate;

	public event Action<int> OnAddTodolist;

	public event Action<int> OnDeleteTodolist;

	public event Action<int> OnCloseTodolist;

	public event Action<int> OnOpenTodolist;

	public new void Init()
	{
		Player player = Player.Instance;
		_isActivate.Value = player.SettingSystem.IsTodoListOn;
		ImportData(player.UserData.todolistUserDatas);
		_uiBringToFront = listHolder.GetComponent<UIBringToFront>();
		GameManager gameManager = Singleton<GameManager>.Instance;
		gameManager.OnGameModeChanged = (Action<EGameMode>)Delegate.Combine(gameManager.OnGameModeChanged, (Action<EGameMode>)delegate(EGameMode mode)
		{
			switch (mode)
			{
			case EGameMode.MiniMode:
				if (player.SettingSystem.IsCloseAllPopupMiniMode)
				{
					_beforeMiniModeActive = _isActivate.Value;
				}
				break;
			case EGameMode.Main:
				if (player.SettingSystem.IsCloseAllPopupMiniMode && _beforeMiniModeActive && !_isActivate.Value)
				{
					Toggle();
				}
				break;
			}
		});
		WaitForInit().Forget();
	}

	public bool IsMaxAmount()
	{
		return _cacheDic.Count >= 2;
	}

	private async UniTaskVoid WaitForInit()
	{
		await UniTask.WaitUntil(() => Singleton<GameManager>.Instance.IsInit);
		ObjectPoolManager objectPoolManager = Singleton<ObjectPoolManager>.Instance;
		List<int> list = new List<int>();
		foreach (KeyValuePair<int, TodolistCache> item in _cacheDic)
		{
			var (key, todolistCache2) = (KeyValuePair<int, TodolistCache>)(ref item);
			list.Add(key);
			if (todolistCache2.IsOpen && _isActivate.Value)
			{
				objectPoolManager.Spawn("UI_TodoList", delegate(GameObject val)
				{
					UI_TodoList component = val.GetComponent<UI_TodoList>();
					component.SetParent(listHolder);
					_activatedTodoList.Add(key, component);
					component.OnSelect += SetTodoListHierarchyIndex;
					component.SetData(_cacheDic[key]);
					component.Show();
				});
			}
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
		Debug.Log($"[TodoListManager] SetAvailableKeys - 사용 가능한 키: {_availableKeys.Count}개, 다음 키: {_nextKey}");
	}

	public void Toggle()
	{
		_isActivate.Value = !_isActivate.Value;
		Singleton<UIManager>.Instance.ui_main.button_todoList.Toggle(_isActivate.Value);
		SettingSystem settingSystem = Player.Instance.SettingSystem;
		settingSystem.SetTodoListOn(_isActivate.Value);
		if (_isActivate.Value)
		{
			bool hasOpenList = false;
			foreach (KeyValuePair<int, TodolistCache> item in _cacheDic)
			{
				var (key, data) = (KeyValuePair<int, TodolistCache>)(ref item);
				if (!data.IsOpen)
				{
					continue;
				}
				if (!_activatedTodoList.TryGetValue(key, out var value))
				{
					Singleton<ObjectPoolManager>.Instance.Spawn("UI_TodoList", delegate(GameObject val)
					{
						UI_TodoList component = val.GetComponent<UI_TodoList>();
						component.SetParent(listHolder);
						_activatedTodoList.Add(key, component);
						data.IsOpen = true;
						hasOpenList = true;
						component.OnSelect += SetTodoListHierarchyIndex;
						component.SetData(_cacheDic[key]);
						component.Show();
					});
				}
				else
				{
					data.IsOpen = true;
					hasOpenList = true;
					value.Show();
				}
			}
			if (hasOpenList)
			{
				return;
			}
			if (_cacheDic.Count > 0)
			{
				if (_cacheDic.ContainsKey(settingSystem.LastClosedTodolistKey))
				{
					OpenTodolist(settingSystem.LastClosedTodolistKey);
					return;
				}
				TodolistCache todolistCache2 = _cacheDic.Values.OrderBy((TodolistCache cache) => cache.SortOrder).FirstOrDefault();
				OpenTodolist(todolistCache2.Key);
			}
			else
			{
				AddNewTodoList();
			}
			return;
		}
		foreach (KeyValuePair<int, UI_TodoList> activatedTodo in _activatedTodoList)
		{
			activatedTodo.Value.Hide();
		}
		Singleton<UIManager>.Instance.UINote.OffNote(eNoteTabType.Todolist);
	}

	public void OffAllTodoList()
	{
		_isActivate.Value = true;
		Toggle();
	}

	private void SetTodoListHierarchyIndex(int key)
	{
		UI_TodoList uI_TodoList = _activatedTodoList[key];
		int siblingIndex = listHolder.childCount - 1;
		uI_TodoList.transform.SetSiblingIndex(siblingIndex);
		_uiBringToFront.OnPointDown();
	}

	private int GetNewKey()
	{
		if (_availableKeys.Count > 0)
		{
			return _availableKeys.Dequeue();
		}
		return _nextKey++;
	}

	private void ReleaseKey(int key)
	{
		_availableKeys.Enqueue(key);
	}

	public bool AddNewTodoList(int skinKey = 1, bool needGridPos = false)
	{
		if (_cacheDic.Count >= 2)
		{
			Debug.Log("[할일 그룹 최대 개수 초과]");
			Singleton<ToastManager>.Instance.ShowToast("Demo_MaxTodoGroup".Localize());
			return false;
		}
		Singleton<ObjectPoolManager>.Instance.Spawn("UI_TodoList", delegate(GameObject val)
		{
			UI_TodoList component = val.GetComponent<UI_TodoList>();
			component.SetParent(listHolder);
			TodolistCache newTodolistData = GetNewTodolistData();
			newTodolistData.IsOpen = true;
			newTodolistData.SkinKey = skinKey;
			if (needGridPos)
			{
				RectTransform lastCreatedUI = _activatedTodoList.Values.OrderByDescending((UI_TodoList todo) => todo.transform.GetSiblingIndex()).FirstOrDefault()?.GetComponent<RectTransform>();
				newTodolistData.Pos = UIGridPositioner.GetGridPosition(component.rectTransform, lastCreatedUI, Const.UI_TODOLIST.InitialSize, Const.UI_TODOLIST.InitialPosition, listHolder);
			}
			newTodolistData.SortOrder = GetNextSortOrder();
			_activatedTodoList.Add(newTodolistData.Key, component);
			component.OnSelect += SetTodoListHierarchyIndex;
			component.SetData(newTodolistData);
			component.Show();
			_curCreateKey = newTodolistData.Key;
			this.OnAddTodolist?.Invoke(newTodolistData.Key);
		});
		return true;
	}

	public TodolistCache GetNewTodolistData()
	{
		int newKey = GetNewKey();
		return GetCache(newKey);
	}

	public List<TodolistUserData> ExportData()
	{
		return _cacheDic.Select((KeyValuePair<int, TodolistCache> x) => x.Value.GetTodolistUserData()).ToList();
	}

	private void ImportData(List<TodolistUserData> items)
	{
		if (TryMigrateLegacyData(items))
		{
			Debug.Log("[TodoListManager] Legacy 데이터 마이그레이션 완료");
			return;
		}
		foreach (TodolistUserData item in items)
		{
			TodolistCache value = new TodolistCache(item);
			_cacheDic.Add(item.Key, value);
		}
		ValidateAndReorderData();
	}

	private bool TryMigrateLegacyData(List<TodolistUserData> items)
	{
		if (items == null || items.Count == 0)
		{
			return false;
		}
		if (items.Any((TodolistUserData item) => item.TodoItems.Count > 0))
		{
			return false;
		}
		try
		{
			return MigrateLegacyTodolistData();
		}
		catch (Exception ex)
		{
			Debug.LogError("[TodoListManager] Legacy 마이그레이션 실패: " + ex.Message);
			return false;
		}
	}

	private bool MigrateLegacyTodolistData()
	{
		try
		{
			if (ES3.KeyExists("UserData"))
			{
				string text = ES3.Load<string>("UserData");
				if (!string.IsNullOrEmpty(text))
				{
					return ProcessTodolistData(text);
				}
			}
			string persistentDataPath = Application.persistentDataPath;
			Debug.Log("[TodoListManager] 현재 경로: " + persistentDataPath);
			string text2 = persistentDataPath.Replace("MiniCozyRoom - Demo", "MiniCozyRoom");
			Debug.Log("[TodoListManager] 이전 경로: " + text2);
			string filePath = Path.Combine(text2, "SaveFile_Demo.es3");
			string filePath2 = Path.Combine(text2, "SaveFile_DemoVer0.00.07.es3");
			if (ES3.FileExists(filePath) && ES3.KeyExists("UserData", filePath))
			{
				string text3 = ES3.Load<string>("UserData", filePath);
				if (!string.IsNullOrEmpty(text3))
				{
					Debug.Log("[TodoListManager] SaveFile_Demo.es3에서 UserData 발견");
					return ProcessTodolistData(text3);
				}
			}
			if (ES3.FileExists(filePath2) && ES3.KeyExists("UserData", filePath2))
			{
				string text4 = ES3.Load<string>("UserData", filePath2);
				if (!string.IsNullOrEmpty(text4))
				{
					Debug.Log("[TodoListManager] SaveFile_DemoVer0.00.07.es3에서 UserData 발견");
					return ProcessTodolistData(text4);
				}
			}
			Debug.Log("[TodoListManager] UserData 키가 어떤 파일에도 존재하지 않음");
			return false;
		}
		catch (Exception ex)
		{
			Debug.LogError("[TodoListManager] Legacy 마이그레이션 중 오류: " + ex.Message);
			return false;
		}
	}

	private bool ProcessTodolistData(string userDataJson)
	{
		try
		{
			JToken jToken = JObject.Parse(userDataJson)["todolistUserDatas"];
			if (jToken == null || !jToken.HasValues)
			{
				Debug.Log("[TodoListManager] Legacy todolist 데이터가 없음");
				return false;
			}
			List<LegacyTodolistUserData> list = jToken.ToObject<List<LegacyTodolistUserData>>();
			if (list == null || list.Count == 0)
			{
				Debug.Log("[TodoListManager] Legacy 데이터 파싱 실패 또는 빈 데이터");
				return false;
			}
			ConvertLegacyToNewStructure(list);
			Debug.Log($"[TodoListManager] Legacy 데이터 {list.Count}개 항목을 새 구조로 변환 완료");
			return true;
		}
		catch (Exception ex)
		{
			Debug.LogError("[TodoListManager] Todolist 데이터 처리 중 오류: " + ex.Message);
			return false;
		}
	}

	private void ConvertLegacyToNewStructure(List<LegacyTodolistUserData> legacyData)
	{
		TodolistUserData todolistUserData = new TodolistUserData(GetNewKey());
		todolistUserData.SetTitle("Main_Todolist".Localize());
		todolistUserData.SetSortOrder(0);
		todolistUserData.SetSkinKey(Player.Instance.UserData.playerUserData.selectedUiSkinKey_TodoList);
		todolistUserData.SetOpenState(_isActivate.Value);
		List<LegacyTodolistUserData> list = legacyData.OrderBy((LegacyTodolistUserData x) => x.index).ToList();
		foreach (LegacyTodolistUserData item in list)
		{
			TodoItemUserData todoItemUserData = new TodoItemUserData(item.key);
			todoItemUserData.Content = item.content;
			todoItemUserData.isComplete = item.isComplete;
			todoItemUserData.isFirstComplete = item.isFirstComplete;
			todoItemUserData.Index = item.index;
			todolistUserData.AddTodoItem(todoItemUserData);
		}
		TodolistCache value = new TodolistCache(todolistUserData);
		_cacheDic.Add(todolistUserData.Key, value);
		Debug.Log($"[TodoListManager] {list.Count}개의 Legacy 항목을 1개 그룹으로 변환");
	}

	public void CloseTodolist(int key)
	{
		if (_activatedTodoList.TryGetValue(key, out var value))
		{
			value.OnSelect -= SetTodoListHierarchyIndex;
			value.Hide();
			CloseWait(value, key).Forget();
		}
	}

	private async UniTaskVoid CloseWait(UI_TodoList obj, int key)
	{
		await AnimationAwaiter.WaitForSpecificAnimation(obj.GetComponent<Animator>(), "uiAnimation_close", 0, 0f, obj.GetCancellationTokenOnDestroy());
		Singleton<ObjectPoolManager>.Instance.DeSpawn(obj.gameObject);
		_cacheDic[key].IsOpen = false;
		_activatedTodoList.Remove(key);
		SettingSystem settingSystem = Player.Instance.SettingSystem;
		UIManager uIManager = Singleton<UIManager>.Instance;
		settingSystem.SetLastClosedTodolistKey(key);
		this.OnCloseTodolist?.Invoke(key);
		if (_activatedTodoList.Count == 0)
		{
			_isActivate.Value = false;
			uIManager.ui_main.button_todoList.Toggle(isOn: false);
			settingSystem.SetTodoListOn(isOn: false);
		}
	}

	public void OpenTodolist(int key)
	{
		if (_activatedTodoList.ContainsKey(key))
		{
			_activatedTodoList[key].Show();
			return;
		}
		Singleton<ObjectPoolManager>.Instance.Spawn("UI_TodoList", delegate(GameObject val)
		{
			UI_TodoList component = val.GetComponent<UI_TodoList>();
			component.SetParent(listHolder);
			component.OnSelect += SetTodoListHierarchyIndex;
			component.SetData(_cacheDic[key]);
			_cacheDic[key].IsOpen = true;
			_activatedTodoList.Add(key, component);
			component.Show();
			this.OnOpenTodolist?.Invoke(key);
		});
	}

	public void CopyTodolist(int key)
	{
		if (!AddNewTodoList())
		{
			return;
		}
		if (_curCreateKey == int.MinValue)
		{
			Debug.LogError("최근 생성 키 오류");
			return;
		}
		TodolistCache cache = GetCache(_curCreateKey);
		TodolistCache cache2 = GetCache(key);
		string title = (cache2.Title.IsNullOrWhitespace() ? cache2.Title : (cache2.Title + "UI_Copied".Localize()));
		cache.Title = title;
		cache.SkinKey = cache2.SkinKey;
		cache.Size = cache2.Size;
		cache.SortOrder = cache2.SortOrder + 1;
		ReorderAfterInsert(cache.SortOrder);
		cache.RemoveTodoItem(cache.TodoItems[0]);
		foreach (TodoItemCache todoItem in cache2.TodoItems)
		{
			TodoItemCache todoItemCache = cache.AddTodoItem();
			todoItemCache.EditContent(todoItem.Content);
			if (todoItem.IsComplete)
			{
				todoItemCache.Complete();
			}
		}
		_activatedTodoList[_curCreateKey].SetData(cache);
	}

	public void TryDeleteTodolist(int key)
	{
		if (_cacheDic.TryGetValue(key, out var _))
		{
			if (_cacheDic.Count <= 1)
			{
				return;
			}
			Singleton<PopupManager>.Instance.Hide(PopupType.Delete);
			if (_activatedTodoList.TryGetValue(key, out var value2))
			{
				value2.ClearAllSlots();
				value2.OnSelect -= SetTodoListHierarchyIndex;
				value2.Hide();
				_activatedTodoList.Remove(key);
				Singleton<ObjectPoolManager>.Instance.DeSpawn(value2.gameObject);
			}
			ReleaseKey(key);
			_cacheDic.Remove(key);
			this.OnDeleteTodolist?.Invoke(key);
			ReorderAfterDelete();
		}
		if (_activatedTodoList.Count == 0)
		{
			UIManager uIManager = Singleton<UIManager>.Instance;
			_isActivate.Value = false;
			uIManager.ui_main.button_todoList.Toggle(isOn: false);
			Player.Instance.SettingSystem.SetTodoListOn(isOn: false);
		}
	}

	public TodolistCache GetCache(int key)
	{
		if (_cacheDic.TryGetValue(key, out var value))
		{
			return value;
		}
		TodolistUserData todolistUserData = new TodolistUserData(key);
		todolistUserData.SetSortOrder(GetNextSortOrder());
		value = new TodolistCache(todolistUserData);
		_cacheDic.Add(key, value);
		return value;
	}

	private int GetNextSortOrder()
	{
		if (_cacheDic.Count == 0)
		{
			return 0;
		}
		return _cacheDic.Values.Max((TodolistCache cache) => cache.SortOrder) + 1;
	}

	private void ValidateAndReorderData()
	{
		List<TodolistCache> list = _cacheDic.Values.ToList();
		list.Sort((TodolistCache a, TodolistCache b) => a.SortOrder.CompareTo(b.SortOrder));
		for (int num = 0; num < list.Count; num++)
		{
			list[num].SortOrder = num;
		}
	}

	private void ReorderAfterDelete()
	{
		List<TodolistCache> list = _cacheDic.Values.OrderBy((TodolistCache cache) => cache.SortOrder).ToList();
		for (int num = 0; num < list.Count; num++)
		{
			list[num].SortOrder = num;
		}
	}

	private void ReorderAfterInsert(int insertOrder)
	{
		foreach (TodolistCache value in _cacheDic.Values)
		{
			if (value.SortOrder >= insertOrder && value.SortOrder != insertOrder)
			{
				value.SortOrder++;
			}
		}
	}

	public void UpdateTodolistOrder(int key, int newOrder)
	{
		if (!_cacheDic.TryGetValue(key, out var _))
		{
			return;
		}
		List<TodolistCache> list = _cacheDic.Values.OrderBy((TodolistCache c) => c.SortOrder).ToList();
		int num = list.FindIndex((TodolistCache c) => c.Key == key);
		if (num == -1)
		{
			return;
		}
		newOrder = Mathf.Clamp(newOrder, 0, list.Count - 1);
		if (num != newOrder)
		{
			TodolistCache item = list[num];
			list.RemoveAt(num);
			list.Insert(newOrder, item);
			for (int num2 = 0; num2 < list.Count; num2++)
			{
				list[num2].SortOrder = num2;
			}
		}
	}

	public List<UserNoteData> GetSortedTodolistDataList()
	{
		return ((IEnumerable<TodolistCache>)_cacheDic.Values.OrderBy((TodolistCache cache) => cache.SortOrder)).Select((Func<TodolistCache, UserNoteData>)((TodolistCache cache) => cache.GetTodolistUserData())).ToList();
	}

	public void ResetSlotPositionAndSize()
	{
		int key;
		UI_TodoList value;
		foreach (KeyValuePair<int, UI_TodoList> activatedTodo in _activatedTodoList)
		{
			activatedTodo.Deconstruct(out key, out value);
			value.ResetDefaultSize();
		}
		int num = 0;
		if (Player.Instance.SettingSystem.CurrentMonitorUserData != null)
		{
			num = Player.Instance.SettingSystem.CurrentMonitorUserData.sidebarSize;
		}
		Vector2 basePosition = Const.UI_TODOLIST.InitialPosition - new Vector2(num, 0f);
		RectTransform rectTransform = null;
		foreach (KeyValuePair<int, UI_TodoList> activatedTodo2 in _activatedTodoList)
		{
			activatedTodo2.Deconstruct(out key, out value);
			UI_TodoList uI_TodoList = value;
			if (!(rectTransform == uI_TodoList.rectTransform))
			{
				uI_TodoList.rectTransform.anchoredPosition = UIGridPositioner.GetGridPosition(uI_TodoList.rectTransform, rectTransform, Const.UI_TODOLIST.InitialSize, basePosition, listHolder);
				uI_TodoList.SavePosData();
				rectTransform = uI_TodoList.rectTransform;
			}
		}
	}

	public void ReSetup()
	{
		foreach (KeyValuePair<int, UI_TodoList> activatedTodo in _activatedTodoList)
		{
			activatedTodo.Value.ReSetup();
		}
	}
}
