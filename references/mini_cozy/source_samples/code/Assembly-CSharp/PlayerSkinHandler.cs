using System.Collections.Generic;
using UnityEngine;
using cozyhouse;
using cozyhouse.data;

public class PlayerSkinHandler
{
	private int _curPresetIndex;

	private CharacterParts _curCharacterParts;

	private List<CharacterParts> _characterPartsList = new List<CharacterParts>(3);

	public const int PRESET_AMOUNT = 3;

	public CharacterParts CurCharacterParts => _curCharacterParts;

	public int CurPresetIndex => _curPresetIndex;

	public void Init(Player player)
	{
		_curPresetIndex = player.UserData.playerUserData.wardrobePresetIndex;
		if (_characterPartsList.Count == 0)
		{
			for (int i = 0; i < 3; i++)
			{
				_characterPartsList.Add(new CharacterParts());
			}
			_curCharacterParts = _characterPartsList[0];
		}
		else
		{
			_curCharacterParts = _characterPartsList[_curPresetIndex];
		}
	}

	public void ChangePreset(int index)
	{
		_curPresetIndex = index;
		Player.Instance.UserData.playerUserData.wardrobePresetIndex = index;
		_curCharacterParts = _characterPartsList[index];
	}

	public List<CharacterParts> ExportPresetData()
	{
		return _characterPartsList;
	}

	public void ImportData(List<CharacterParts> items)
	{
		Player instance = Player.Instance;
		if (items.Count == 0)
		{
			for (int i = 0; i < 3; i++)
			{
				if (i == 0)
				{
					_characterPartsList.Add(instance.UserData.characterUserData.characterParts);
				}
				else
				{
					_characterPartsList.Add(new CharacterParts());
				}
			}
			Debug.Log("[PlayerSkinHandler] 스킨 프리셋 마이그레이션 완료");
		}
		else
		{
			_characterPartsList.AddRange(items);
			Debug.Log($"[PlayerSkinHandler] 스킨 프리셋 데이터 로드 완료 {items.Count}개");
		}
	}

	public bool TryUnlockPcSkin(int skinKey)
	{
		if (CheckUnlockPcSkin(skinKey))
		{
			Debug.LogWarning("[Game] 이미 해금된 pc 스킨을 해금하려 합니다.");
			return false;
		}
		GameInfo instance = GameInfo.Instance;
		if (!Player.Instance.TrySpendLofiPoint(instance.PcSkins[skinKey].Cost))
		{
			Debug.Log("[Game] pc 스킨을 구매할 골드가 충분하지 않습니다.");
			return false;
		}
		if (instance.PcSkins.TryGetValue(skinKey, out var value))
		{
			if (value.DecorableType.Equals("Hair") && value.GetGroupKeys().Count > 0)
			{
				UnlockPcSkinGroup(value.DecorableType, value.GetGroupKeys()[0]);
			}
			else
			{
				UnlockPcSkin(skinKey);
			}
			return true;
		}
		Debug.Log("[Game] pc 스킨 정보가 없습니다.");
		return false;
	}

	private void UnlockPcSkinGroup(string DecorableType, int groupKey)
	{
		Player instance = Player.Instance;
		PlayerUserData playerUserData = instance.UserData.playerUserData;
		foreach (PcSkinInfo value in GameInfo.Instance.PcSkins.Values)
		{
			if (value.DecorableType == DecorableType && value.GetGroupKeys().Count > 0 && value.GetGroupKeys().Contains(groupKey) && !CheckUnlockPcSkin(value.PcSkinKey))
			{
				playerUserData.unlockedPcSkinKeyList.Add(value.PcSkinKey);
			}
		}
		instance.Save();
		Debug.Log($"[Game] pc 스킨 그룹이 해금되었습니다. pc skin group key = {groupKey}");
	}

	public void UnlockPcSkin(int skinKey)
	{
		Player instance = Player.Instance;
		instance.UserData.playerUserData.unlockedPcSkinKeyList.Add(skinKey);
		instance.Save();
		Debug.Log($"[Game] 스킨이 해금되었습니다. pc skin key = {skinKey}");
	}

	public void UnlockPcSkin_WithoutSave(int skinKey)
	{
		Player.Instance.UserData.playerUserData.unlockedPcSkinKeyList.Add(skinKey);
		Debug.Log($"[Game] 스킨이 해금되었습니다. pc skin key = {skinKey}");
	}

	public bool CheckUnlockPcSkin(int skinKey)
	{
		return Player.Instance.UserData.playerUserData.unlockedPcSkinKeyList.Contains(skinKey);
	}

	public int GetEquippedSkinKey(string decoType)
	{
		return decoType switch
		{
			"Body" => _curCharacterParts.body, 
			"Hair" => _curCharacterParts.hair, 
			"Eyes" => _curCharacterParts.eyes, 
			"Mouth" => _curCharacterParts.mouth, 
			"Hat" => _curCharacterParts.hat, 
			"Top" => _curCharacterParts.top, 
			"Bottom" => _curCharacterParts.bottom, 
			"Shoes" => _curCharacterParts.shoes, 
			"Accessory1" => _curCharacterParts.accessory1, 
			"Accessory2" => _curCharacterParts.accessory2, 
			_ => -1, 
		};
	}

	public bool CheckEquippedSkin(string decoType, int skinKey)
	{
		switch (decoType)
		{
		case "Body":
			if (!_curCharacterParts.body.Equals(skinKey))
			{
				return false;
			}
			return true;
		case "Hair":
			if (!_curCharacterParts.hair.Equals(skinKey))
			{
				return false;
			}
			return true;
		case "Eyes":
			if (!_curCharacterParts.eyes.Equals(skinKey))
			{
				return false;
			}
			return true;
		case "Mouth":
			if (!_curCharacterParts.mouth.Equals(skinKey))
			{
				return false;
			}
			return true;
		case "Hat":
			if (!_curCharacterParts.hat.Equals(skinKey))
			{
				return false;
			}
			return true;
		case "Top":
			if (!_curCharacterParts.top.Equals(skinKey))
			{
				return false;
			}
			return true;
		case "Bottom":
			if (!_curCharacterParts.bottom.Equals(skinKey))
			{
				return false;
			}
			return true;
		case "Shoes":
			if (!_curCharacterParts.shoes.Equals(skinKey))
			{
				return false;
			}
			return true;
		case "Accessory1":
			if (!_curCharacterParts.accessory1.Equals(skinKey))
			{
				return false;
			}
			return true;
		case "Accessory2":
			if (!_curCharacterParts.accessory2.Equals(skinKey))
			{
				return false;
			}
			return true;
		default:
			return false;
		}
	}
}
